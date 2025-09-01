using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class GridBasedRobotAI : MonoBehaviour
{
    #region Clases Internas y Variables
    private class PathNode
    {
        public Vector2Int position;
        public int gCost, hCost; 
        public PathNode parent;
        public int FCost => gCost + hCost;
        public PathNode(Vector2Int pos) { this.position = pos; }
    }

    [Header("ConfiguraciÃ³n (Configurado por RobotSpawner)")]
    public Transform plane;
    public Color targetColor;
    public int robotID;
    public float moveSpeed = 12f;
    public float cellDelay = 0.05f;

    [Header("Estado Actual - Solo Lectura")]
    [SerializeField] private GridRobotState currentState = GridRobotState.EXPLORING;
    [SerializeField] private int gemsDelivered = 0;
    [SerializeField] private int totalMoves = 0;
    [SerializeField] private Vector2Int currentGridPos;
    [SerializeField] private GameObject carriedGem = null;

    [Header("DetecciÃ³n y Capas")]
    public LayerMask gemLayerMask = 1 << 9;

    [Header("Anti-Bloqueo")]
    [Range(5f, 30f)] public float stuckDetectionTime = 10f;
    [Range(3, 10)] public int maxPathRetries = 5;
    [Range(10f, 60f)] public float forceExplorationTime = 20f;

    [Header("Debug")]
    public bool showDebugLogs = false;
    public bool showMovementGizmos = false;
    [Header("Sistema de Colisiones")]
    private RobotCollisionManager collisionManager;

    // Variables privadas
    private bool isMoving = false;
    private bool isInitialized = false;
    private List<Vector2Int> currentPath;
    private int currentPathIndex;
    private HashSet<Vector2Int> visitedCells = new HashSet<Vector2Int>();
    private Vector2Int? targetGemCell = null;
    private Vector2Int? deliveryZonePos = null;
    
    private int gridSize;
    private Vector2Int homeGridPos;
    private readonly Vector2Int[] directions = { 
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left 
    };

    // Anti-bloqueo variables
    private float lastMoveTime;
    private float lastStateChangeTime;
    private Vector2Int lastPosition;
    private int pathRetryCount = 0;
    private List<Vector2Int> recentlyFailedTargets = new List<Vector2Int>();
    private float lastGemSearchTime;

    // Propiedades pÃºblicas
    public GridRobotState CurrentState => currentState;
    public int GemsDelivered => gemsDelivered;
    public int TotalMoves => totalMoves;
    public Vector2Int CurrentGridPos => currentGridPos;
    public GameObject CarriedGem => carriedGem;
    #endregion

    #region InicializaciÃ³n
    void Start()
    {
        StartCoroutine(InitializeWhenReady());
    }

    IEnumerator InitializeWhenReady()
    {
        while (GridManager.Instance == null)
        {
            yield return new WaitForSeconds(0.1f);
        }

        yield return new WaitForSeconds(0.2f);
        InitializeRobot();
        StartCoroutine(AIRoutine());
        StartCoroutine(AntiStuckRoutine()); // Nueva rutina anti-bloqueo
    }

    void InitializeRobot()
    {
        gridSize = GridManager.Instance.gridSize;
        currentGridPos = WorldToGrid(transform.position);
        homeGridPos = currentGridPos;
        lastPosition = currentGridPos;

        // AGREGAR ESTA LÍNEA:
        collisionManager = FindObjectOfType<RobotCollisionManager>();

        if (!GridManager.Instance.IsCellWalkable(currentGridPos))
        {
            LogError($"Robot {robotID} spawneado en posición inválida {currentGridPos}. Buscando alternativa...");
            currentGridPos = GridManager.Instance.FindNearestWalkablePosition(currentGridPos);
            transform.position = GridToWorld(currentGridPos);
        }

        transform.position = GridToWorld(currentGridPos);
        visitedCells.Add(currentGridPos);
        FindDeliveryZone();

        // Inicializar timers anti-bloqueo
        lastMoveTime = Time.time;
        lastStateChangeTime = Time.time;
        lastGemSearchTime = Time.time;

        isInitialized = true;
        LogDebug($"Robot {robotID} inicializado en {currentGridPos}");
    }

    void FindDeliveryZone()
    {
        var deliveryZones = GameObject.FindGameObjectsWithTag("DeliveryZone");
        
        foreach (var zone in deliveryZones)
        {
            var renderer = zone.GetComponent<Renderer>();
            if (renderer != null && ColorsMatch(renderer.material.color, targetColor))
            {
                deliveryZonePos = WorldToGrid(zone.transform.position);
                LogDebug($"Robot {robotID} encontrÃ³ zona de entrega en {deliveryZonePos}");
                return;
            }
        }

        deliveryZonePos = homeGridPos;
        LogDebug($"Robot {robotID} usando posiciÃ³n home como zona de entrega");
    }
    #endregion

    #region Rutinas Principales
    IEnumerator AIRoutine()
    {
        while (!isInitialized)
        {
            yield return new WaitForSeconds(0.1f);
        }

        while (true)
        {
            yield return new WaitForSeconds(cellDelay);
            
            if (!isMoving)
            {
                if (currentPath != null && currentPathIndex < currentPath.Count) 
                {
                    FollowPath();
                }
                else 
                {
                    ExecuteStateLogic();
                }
            }
        }
    }

    // Nueva rutina para prevenir bloqueos
    IEnumerator AntiStuckRoutine()
    {
        while (!isInitialized)
        {
            yield return new WaitForSeconds(1f);
        }

        while (true)
        {
            yield return new WaitForSeconds(2f); // Verificar cada 2 segundos
            
            // Detectar si el robot estÃ¡ atascado
            if (IsRobotStuck())
            {
                HandleStuckRobot();
            }

            // Limpiar targets fallidos antiguos
            CleanupFailedTargets();

            // Forzar bÃºsqueda de gemas si hace mucho que no encuentra ninguna
            if (carriedGem == null && Time.time - lastGemSearchTime > forceExplorationTime)
            {
                ForceGemSearch();
            }
        }
    }

    bool IsRobotStuck()
    {
        // Verificar si no se ha movido en mucho tiempo
        bool notMovingLong = Time.time - lastMoveTime > stuckDetectionTime;
        
        // Verificar si estÃ¡ en el mismo estado mucho tiempo sin progreso
        bool stuckInState = Time.time - lastStateChangeTime > stuckDetectionTime * 1.5f;
        
        // Verificar si estÃ¡ en la misma posiciÃ³n
        bool samePosition = currentGridPos == lastPosition;
        
        return (notMovingLong && samePosition) || stuckInState;
    }

    void HandleStuckRobot()
    {
        LogDebug($"Robot {robotID} detectado como atascado. Reinitiando comportamiento...");
        
        // Resetear estado
        currentPath = null;
        currentPathIndex = 0;
        targetGemCell = null;
        pathRetryCount = 0;
        
        // Cambiar a exploraciÃ³n forzada
        currentState = GridRobotState.EXPLORING;
        lastStateChangeTime = Time.time;
        
        // Si estÃ¡ cargando una gema, intentar entregarla
        if (carriedGem != null)
        {
            currentState = GridRobotState.RETURNING_HOME;
            LogDebug($"Robot {robotID} atascado con gema, intentando entrega directa");
        }
    }

    void CleanupFailedTargets()
    {
        // Limpiar targets que fallaron hace mÃ¡s de 30 segundos
        recentlyFailedTargets.RemoveAll(target => 
            Vector2Int.Distance(target, currentGridPos) > 10);
    }

    void ForceGemSearch()
    {
        LogDebug($"Robot {robotID} forzando bÃºsqueda de gemas...");
        
        // Buscar gemas en toda la escena
        var allGems = GameObject.FindGameObjectsWithTag("Gem")
            .Where(g => g.layer == 9 && IsGemMyColor(g))
            .ToArray();

        if (allGems.Length > 0)
        {
            // Elegir la gema mÃ¡s cercana
            var closestGem = allGems
                .OrderBy(g => Vector2Int.Distance(currentGridPos, WorldToGrid(g.transform.position)))
                .First();

            targetGemCell = WorldToGrid(closestGem.transform.position);
            currentState = GridRobotState.MOVING_TO_GEM;
            lastStateChangeTime = Time.time;
            
            LogDebug($"Robot {robotID} encontrÃ³ gema objetivo en {targetGemCell}");
        }
        else
        {
            // No hay mÃ¡s gemas de su color, continuar explorando
            currentState = GridRobotState.EXPLORING;
            LogDebug($"Robot {robotID} no encontrÃ³ mÃ¡s gemas de su color");
        }
        
        lastGemSearchTime = Time.time;
    }
    #endregion

    #region LÃ³gica de Estados Mejorada
    void ExecuteStateLogic()
    {
        // Actualizar posiciÃ³n para detecciÃ³n de bloqueo
        if (currentGridPos != lastPosition)
        {
            lastPosition = currentGridPos;
            lastMoveTime = Time.time;
        }

        currentPath = null;
        currentPathIndex = 0;

        switch (currentState)
        {
            case GridRobotState.EXPLORING:
                HandleExploringState();
                break;

            case GridRobotState.MOVING_TO_GEM:
                HandleMovingToGemState();
                break;

            case GridRobotState.RETURNING_HOME:
                HandleReturningHomeState();
                break;

            case GridRobotState.WAITING:
                HandleWaitingState();
                break;
        }
    }

    void HandleExploringState()
    {
        // Buscar gemas primero
        if (carriedGem == null && DetectGemInAdjacents())
        {
            ChangeState(GridRobotState.MOVING_TO_GEM);
            return;
        }

        // Buscar gemas mÃ¡s lejanas si no hay adyacentes
        if (carriedGem == null && SearchForDistantGems())
        {
            ChangeState(GridRobotState.MOVING_TO_GEM);
            return;
        }

        // Continuar explorando
        Explore();
    }

    void HandleMovingToGemState()
    {
        if (!targetGemCell.HasValue)
        {
            ChangeState(GridRobotState.EXPLORING);
            return;
        }

        // Verificar si el target estÃ¡ en la lista de fallidos recientes
        if (recentlyFailedTargets.Contains(targetGemCell.Value))
        {
            LogDebug($"Robot {robotID}: Target {targetGemCell} en lista de fallidos, buscando otro");
            targetGemCell = null;
            ChangeState(GridRobotState.EXPLORING);
            return;
        }

        float distanceToGem = Vector2Int.Distance(currentGridPos, targetGemCell.Value);
        
        if (distanceToGem <= 1.1f)
        {
            AttemptGemPickup();
        }
        else
        {
            // Intentar crear ruta
            currentPath = FindPath_AStar(currentGridPos, targetGemCell.Value);
            if (currentPath == null || currentPath.Count == 0)
            {
                pathRetryCount++;
                if (pathRetryCount >= maxPathRetries)
                {
                    LogDebug($"Robot {robotID}: Demasiados fallos de ruta a {targetGemCell}, abandonando target");
                    recentlyFailedTargets.Add(targetGemCell.Value);
                    targetGemCell = null;
                    pathRetryCount = 0;
                    ChangeState(GridRobotState.EXPLORING);
                }
            }
            else
            {
                pathRetryCount = 0; // Reset si encontrÃ³ ruta
            }
        }
    }

    void HandleReturningHomeState()
    {
        if (carriedGem == null)
        {
            ChangeState(GridRobotState.EXPLORING);
            return;
        }

        Vector2Int deliveryPos = deliveryZonePos ?? homeGridPos;
        
        if (currentGridPos == deliveryPos)
        {
            DeliverGem();
        }
        else
        {
            currentPath = FindPath_AStar(currentGridPos, deliveryPos);
            if (currentPath == null && pathRetryCount < maxPathRetries)
            {
                pathRetryCount++;
                LogDebug($"Robot {robotID}: Reintentando ruta a entrega (intento {pathRetryCount})");
            }
            else if (currentPath == null)
            {
                // Si no puede llegar a la zona de entrega, intentar entregar en posiciÃ³n actual
                LogDebug($"Robot {robotID}: No puede llegar a zona de entrega, entregando aquÃ­");
                DeliverGem();
            }
        }
    }

    void HandleWaitingState()
    {
        // En espera, buscar gemas periÃ³dicamente
        if (carriedGem == null && Time.time - lastGemSearchTime > 5f)
        {
            if (DetectGemInAdjacents() || SearchForDistantGems())
            {
                ChangeState(GridRobotState.MOVING_TO_GEM);
            }
            else
            {
                ChangeState(GridRobotState.EXPLORING);
            }
            lastGemSearchTime = Time.time;
        }
    }

    void ChangeState(GridRobotState newState)
    {
        if (currentState != newState)
        {
            LogDebug($"Robot {robotID}: {currentState} â†’ {newState}");
            currentState = newState;
            lastStateChangeTime = Time.time;
        }
    }
    #endregion

    #region BÃºsqueda de Gemas Mejorada
    bool SearchForDistantGems()
    {
        if (carriedGem != null) return false;

        var availableGems = GameObject.FindGameObjectsWithTag("Gem")
            .Where(g => g.layer == 9 && IsGemMyColor(g))
            .Where(g => !recentlyFailedTargets.Contains(WorldToGrid(g.transform.position)))
            .ToArray();

        if (availableGems.Length == 0) return false;

        // Ordenar por distancia y elegir una de las 3 mÃ¡s cercanas (para evitar que todos vayan a la misma)
        var nearbyGems = availableGems
            .OrderBy(g => Vector2Int.Distance(currentGridPos, WorldToGrid(g.transform.position)))
            .Take(3)
            .ToArray();

        if (nearbyGems.Length > 0)
        {
            var selectedGem = nearbyGems[Random.Range(0, nearbyGems.Length)];
            targetGemCell = WorldToGrid(selectedGem.transform.position);
            LogDebug($"Robot {robotID}: Gema distante encontrada en {targetGemCell}");
            return true;
        }

        return false;
    }

    bool DetectGemInAdjacents()
    {
        foreach (var neighborPos in GetWalkableNeighbors(currentGridPos))
        {
            Vector3 worldPos = GridToWorld(neighborPos);
            float detectionRadius = 0.7f;

            var colliders = Physics.OverlapSphere(worldPos, detectionRadius, gemLayerMask);
            
            foreach (var collider in colliders)
            {
                if (IsGemMyColor(collider.gameObject))
                {
                    targetGemCell = neighborPos;
                    LogDebug($"Robot {robotID}: Gema detectada en posiciÃ³n adyacente {neighborPos}");
                    return true;
                }
            }
        }
        return false;
    }
    #endregion

    #region ExploraciÃ³n Mejorada
    void Explore()
    {
        Vector2Int? target = FindBestExplorationTarget();
        
        if (target.HasValue)
        {
            currentPath = FindPath_AStar(currentGridPos, target.Value);
            if (currentPath == null)
            {
                LogDebug($"Robot {robotID}: No hay ruta a objetivo de exploraciÃ³n {target}, eligiendo otro");
                // Intentar con objetivo aleatorio
                target = FindRandomUnvisitedCell();
                if (target.HasValue)
                {
                    currentPath = FindPath_AStar(currentGridPos, target.Value);
                }
            }
        }
        else
        {
            // Si no hay mÃ¡s celdas que explorar, cambiar a modo espera activa
            ChangeState(GridRobotState.WAITING);
        }
    }

    Vector2Int? FindBestExplorationTarget()
    {
        // Estrategia 1: Buscar fronteras
        var frontierCells = GetFrontierCells();
        if (frontierCells.Count > 0)
        {
            return frontierCells.OrderBy(cell => Vector2Int.Distance(currentGridPos, cell)).First();
        }

        // Estrategia 2: Buscar celdas no visitadas
        return FindRandomUnvisitedCell();
    }

    List<Vector2Int> GetFrontierCells()
    {
        var frontierCells = new List<Vector2Int>();
        
        foreach (var visitedCell in visitedCells)
        {
            foreach (var dir in directions)
            {
                Vector2Int neighbor = visitedCell + dir;
                if (GridManager.Instance.IsCellWalkable(neighbor) && 
                    !visitedCells.Contains(neighbor) && 
                    !frontierCells.Contains(neighbor))
                {
                    frontierCells.Add(neighbor);
                }
            }
        }

        return frontierCells;
    }

    Vector2Int? FindRandomUnvisitedCell()
    {
        var allUnvisited = new List<Vector2Int>();
        
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (GridManager.Instance.IsCellWalkable(cell) && !visitedCells.Contains(cell))
                {
                    allUnvisited.Add(cell);
                }
            }
        }

        return allUnvisited.Count > 0 ? allUnvisited[Random.Range(0, allUnvisited.Count)] : null;
    }
    #endregion

    #region MÃ©todos Existentes (sin cambios significativos)
    void AttemptGemPickup()
    {
        if (!targetGemCell.HasValue) return;

        Vector3 targetWorldPos = GridToWorld(targetGemCell.Value);
        float pickupRadius = 0.8f;

        var colliders = Physics.OverlapSphere(targetWorldPos, pickupRadius, gemLayerMask);
        GameObject gemToPick = null;

        foreach (var collider in colliders)
        {
            if (IsGemMyColor(collider.gameObject))
            {
                gemToPick = collider.gameObject;
                break;
            }
        }

        if (gemToPick != null)
        {
            carriedGem = gemToPick;
            gemToPick.transform.SetParent(transform);
            gemToPick.transform.localPosition = new Vector3(0, 1.2f, 0);
            
            var gemCollider = gemToPick.GetComponent<Collider>();
            if (gemCollider != null) gemCollider.enabled = false;

            ChangeState(GridRobotState.RETURNING_HOME);
            targetGemCell = null;
            pathRetryCount = 0;

            LogDebug($"Robot {robotID}: Gema recogida exitosamente");
        }
        else
        {
            LogDebug($"Robot {robotID}: Gema no encontrada en {targetGemCell}");
            if (targetGemCell.HasValue)
            {
                recentlyFailedTargets.Add(targetGemCell.Value);
            }
            targetGemCell = null;
            ChangeState(GridRobotState.EXPLORING);
        }
    }

    void DeliverGem()
    {
        if (carriedGem == null) return;

        Vector2Int deliveryPos = deliveryZonePos ?? homeGridPos;
        Vector3 deliveryWorldPos = GridToWorld(deliveryPos);

        carriedGem.transform.SetParent(null);
        carriedGem.transform.position = deliveryWorldPos + Vector3.up * 0.1f;
        
        var gemCollider = carriedGem.GetComponent<Collider>();
        if (gemCollider != null) gemCollider.enabled = true;
        carriedGem.layer = 0;

        gemsDelivered++;
        carriedGem = null;
        pathRetryCount = 0;

        ChangeState(GridRobotState.EXPLORING);

        LogDebug($"Robot {robotID}: Gema entregada. Total: {gemsDelivered}");
    }

    void FollowPath()
    {
        if (currentPath == null || currentPathIndex >= currentPath.Count) return;
        
        Vector2Int nextPos = currentPath[currentPathIndex];
        StartCoroutine(MoveToCell(nextPos, () => { 
            currentPathIndex++;
        }));
    }
    #endregion

    #region A* y Movimiento (sin cambios)
    private List<Vector2Int> FindPath_AStar(Vector2Int start, Vector2Int end)
    {
        if (start == end) return new List<Vector2Int>();

        PathNode startNode = new PathNode(start) { 
            gCost = 0, 
            hCost = CalculateDistance(start, end) 
        };

        List<PathNode> openSet = new List<PathNode> { startNode };
        HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();

        while (openSet.Count > 0)
        {
            PathNode currentNode = openSet.OrderBy(n => n.FCost).ThenBy(n => n.hCost).First();

            if (currentNode.position == end)
            {
                return RetracePath(startNode, currentNode);
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode.position);

            foreach (var neighborPos in GetWalkableNeighbors(currentNode.position))
            {
                if (closedSet.Contains(neighborPos)) continue;

                int tentativeGCost = currentNode.gCost + 10;
                PathNode neighborNode = openSet.FirstOrDefault(n => n.position == neighborPos);

                if (neighborNode == null)
                {
                    neighborNode = new PathNode(neighborPos)
                    {
                        gCost = tentativeGCost,
                        hCost = CalculateDistance(neighborPos, end),
                        parent = currentNode
                    };
                    openSet.Add(neighborNode);
                }
                else if (tentativeGCost < neighborNode.gCost)
                {
                    neighborNode.gCost = tentativeGCost;
                    neighborNode.parent = currentNode;
                }
            }
        }

        return null;
    }

    private List<Vector2Int> RetracePath(PathNode startNode, PathNode endNode)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        PathNode currentNode = endNode;

        while (currentNode != startNode)
        {
            path.Add(currentNode.position);
            currentNode = currentNode.parent;
        }

        path.Reverse();
        return path;
    }

    IEnumerator MoveToCell(Vector2Int targetPos, System.Action onComplete)
    {
        // Verificación inicial
        if (collisionManager != null && !collisionManager.CanRobotMove(robotID))
        {
            LogDebug($"Robot {robotID}: Movimiento bloqueado por sistema de colisiones");
            yield return new WaitForSeconds(0.1f);
            onComplete?.Invoke();
            yield break;
        }

        isMoving = true;
        totalMoves++;

        Vector3 startWorldPos = transform.position;
        Vector3 endWorldPos = GridToWorld(targetPos);

        Vector3 direction = endWorldPos - startWorldPos;
        if (direction.magnitude > 0.1f)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        float journeyTime = 1f / moveSpeed;
        float elapsedTime = 0f;

        while (elapsedTime < journeyTime)
        {
            // VERIFICACIÓN CONTINUA DE COLISIONES DURANTE EL MOVIMIENTO
            if (collisionManager != null && !collisionManager.CanRobotMove(robotID))
            {
                LogDebug($"Robot {robotID}: Movimiento interrumpido por colisión en progreso");
                isMoving = false;
                onComplete?.Invoke();
                yield break;
            }

            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / journeyTime);
            
            transform.position = Vector3.Lerp(startWorldPos, endWorldPos, t);
            yield return null;
        }

        transform.position = endWorldPos;
        currentGridPos = targetPos;
        visitedCells.Add(targetPos);

        isMoving = false;
        onComplete?.Invoke();
    }
    #endregion

    #region MÃ©todos de Utilidad
    private List<Vector2Int> GetWalkableNeighbors(Vector2Int pos)
    {
        return directions
            .Select(dir => pos + dir)
            .Where(neighbor => GridManager.Instance.IsCellWalkable(neighbor))
            .ToList();
    }

    public void InterruptCurrentMovement()
    {
        if (isMoving)
        {
            StopAllCoroutines(); // Detener todas las corrutinas incluyendo MoveToCell
            isMoving = false;
            LogDebug($"Robot {robotID}: Movimiento interrumpido por colisión");
        }
    }

    private int CalculateDistance(Vector2Int a, Vector2Int b)
    {
        return 10 * (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y));
    }

    private Vector2Int WorldToGrid(Vector3 worldPos)
    {
        if (GridManager.Instance != null)
        {
            return GridManager.Instance.WorldToGrid(worldPos);
        }
        
        Vector3 planeCenter = plane != null ? plane.position : Vector3.zero;
        Vector3 worldScale = plane != null ? plane.lossyScale : Vector3.one;
        float cellWidth = (10f * worldScale.x) / gridSize;
        float cellHeight = (10f * worldScale.z) / gridSize;
        
        Vector3 localPos = worldPos - planeCenter;
        int x = Mathf.Clamp(
            Mathf.RoundToInt((localPos.x / cellWidth) + ((gridSize - 1) * 0.5f)), 
            0, gridSize - 1
        );
        int y = Mathf.Clamp(
            Mathf.RoundToInt((localPos.z / cellHeight) + ((gridSize - 1) * 0.5f)), 
            0, gridSize - 1
        );
        return new Vector2Int(x, y);
    }

    private Vector3 GridToWorld(Vector2Int gridPos)
    {
        if (GridManager.Instance != null)
        {
            return GridManager.Instance.GridToWorld(gridPos);
        }
        
        Vector3 planeCenter = plane != null ? plane.position : Vector3.zero;
        Vector3 worldScale = plane != null ? plane.lossyScale : Vector3.one;
        float cellWidth = (10f * worldScale.x) / gridSize;
        float cellHeight = (10f * worldScale.z) / gridSize;
        
        float offsetX = (gridPos.x - (gridSize - 1) * 0.5f) * cellWidth;
        float offsetZ = (gridPos.y - (gridSize - 1) * 0.5f) * cellHeight;
        float y = plane != null ? plane.position.y + 0.5f : 0.5f;
        
        return new Vector3(planeCenter.x + offsetX, y, planeCenter.z + offsetZ);
    }

    private bool IsGemMyColor(GameObject gem)
    {
        var renderer = gem.GetComponent<Renderer>();
        if (renderer == null) return false;
        
        return ColorsMatch(renderer.material.color, targetColor);
    }

    private bool ColorsMatch(Color a, Color b)
    {
        float threshold = 0.1f;
        return Vector4.SqrMagnitude((Vector4)a - (Vector4)b) < threshold;
    }

    private void LogDebug(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[RobotAI] {message}");
        }
    }

    private void LogError(string message)
    {
        Debug.LogError($"[RobotAI] {message}", this);
    }
    #endregion

    #region Debug y Gizmos
    void OnDrawGizmos()
    {
        if (!showMovementGizmos || !isInitialized) return;

        Gizmos.color = targetColor;
        Gizmos.DrawWireCube(GridToWorld(currentGridPos), Vector3.one * 0.8f);

        if (currentPath != null && currentPath.Count > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < currentPath.Count - 1; i++)
            {
                Vector3 start = GridToWorld(currentPath[i]);
                Vector3 end = GridToWorld(currentPath[i + 1]);
                Gizmos.DrawLine(start, end);
            }
        }

        if (targetGemCell.HasValue)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(GridToWorld(targetGemCell.Value), 0.5f);
        }

        if (deliveryZonePos.HasValue)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(GridToWorld(deliveryZonePos.Value), Vector3.one * 1.2f);
        }
    }

    [ContextMenu("Show Robot Status")]
    void ShowRobotStatus()
    {
        Debug.Log($"=== Robot {robotID} Status ===");
        Debug.Log($"State: {currentState}");
        Debug.Log($"Position: {currentGridPos}");
        Debug.Log($"Gems Delivered: {gemsDelivered}");
        Debug.Log($"Total Moves: {totalMoves}");
        Debug.Log($"Carrying Gem: {carriedGem != null}");
        Debug.Log($"Visited Cells: {visitedCells.Count}");
        Debug.Log($"Target Gem: {targetGemCell}");
        Debug.Log($"Delivery Zone: {deliveryZonePos}");
        Debug.Log($"Failed Targets: {recentlyFailedTargets.Count}");
        Debug.Log($"Last Move Time: {Time.time - lastMoveTime:F1}s ago");
        Debug.Log($"Path Retry Count: {pathRetryCount}");
    }
    #endregion
}