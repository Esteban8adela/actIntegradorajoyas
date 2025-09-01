using UnityEngine;
using System.Collections.Generic;

public class RobotSpawner : MonoBehaviour
{
    [Header("Escena / Referencias")]
    public Transform plane;
    public GameObject robotPrefab;

    [Header("ConfiguraciÃ³n de Robots")]
    [Tooltip("TamaÃ±o del robot (escala uniforme).")]
    [Range(0.3f, 3f)] public float robotScale = 0.8f;
    
    [Tooltip("Altura sobre el piso.")]
    [Range(0f, 3f)] public float heightOffset = 0.4f;

    [Header("Colores de Robots (deben coincidir con las gemas)")]
    public Color[] robotColors = new Color[3]
    {
        new Color(0.90f, 0.10f, 0.10f, 1f), // Rojo
        new Color(0.10f, 0.55f, 0.95f, 1f), // Azul
        new Color(0.20f, 0.80f, 0.25f, 1f), // Verde
    };

    [Header("ConfiguraciÃ³n de IA")]
    public float robotMoveSpeed = 12f;
    public float cellDelay = 0.05f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    void Start()
    {
        // PequeÃ±o delay para asegurar que GridManager estÃ© listo
        Invoke(nameof(SpawnRobots), 0.15f);
    }

    [ContextMenu("Spawn Robots Now")]
    public void SpawnRobots()
    {
        if (!ValidateReferences()) return;

        // Obtener gridSize del GridManager
        int gridSize = GridManager.Instance.gridSize;

        // Limpiar robots existentes
        ClearExistingRobots();
        
        // Crear contenedor
        var holder = new GameObject("RobotsHolder");

        // Posiciones predefinidas en las esquinas
        Vector2Int[] preferredPositions = {
            new Vector2Int(0, 0),                    // Esquina inferior-izquierda
            new Vector2Int(gridSize - 1, 0),         // Esquina inferior-derecha  
            new Vector2Int(0, gridSize - 1)          // Esquina superior-izquierda
        };

        // Crear los 3 robots
        for (int i = 0; i < 3 && i < robotColors.Length; i++)
        {
            Vector2Int spawnGridPos = FindValidSpawnPosition(preferredPositions[i], gridSize);
            Vector3 worldPos = GridManager.Instance.GridToWorld(spawnGridPos);
            worldPos.y += heightOffset;

            CreateRobot(worldPos, holder.transform, i, spawnGridPos);
        }

        if (showDebugInfo)
        {
            Debug.Log($"[RobotSpawner] âœ“ Creados 3 robots con Grid Size: {gridSize}");
        }
    }

    bool ValidateReferences()
    {
        // Buscar el plane si no estÃ¡ asignado
        if (plane == null) 
        {
            plane = GameObject.Find("Plane")?.transform;
            if (plane == null)
            {
                Debug.LogError("[RobotSpawner] Â¡No se encontrÃ³ el Plane en la escena!");
                return false;
            }
        }

        // Verificar GridManager
        if (GridManager.Instance == null)
        {
            Debug.LogError("[RobotSpawner] Â¡GridManager no estÃ¡ inicializado!");
            return false;
        }

        // Verificar prefab del robot
        if (robotPrefab == null)
        {
            Debug.LogError("[RobotSpawner] Â¡Asigna el Prefab del Robot en el Inspector!");
            return false;
        }

        // Verificar que el prefab tenga el componente de IA
        var aiComponent = robotPrefab.GetComponent<GridBasedRobotAI>();
        if (aiComponent == null)
        {
            Debug.LogError("[RobotSpawner] Â¡El prefab del Robot debe tener el componente GridBasedRobotAI!");
            return false;
        }

        return true;
    }

    Vector2Int FindValidSpawnPosition(Vector2Int preferredPos, int gridSize)
    {
        // Primero intentar la posiciÃ³n preferida
        if (GridManager.Instance.IsCellWalkable(preferredPos))
        {
            return preferredPos;
        }

        if (showDebugInfo)
        {
            Debug.LogWarning($"[RobotSpawner] PosiciÃ³n preferida {preferredPos} no es vÃ¡lida, buscando alternativa...");
        }

        // Buscar en espiral desde la posiciÃ³n preferida
        int searchRadius = 1;
        while (searchRadius < Mathf.Max(gridSize / 2, 5))
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    // Solo verificar el borde del cuadrado de bÃºsqueda
                    if (Mathf.Abs(dx) != searchRadius && Mathf.Abs(dy) != searchRadius)
                        continue;

                    Vector2Int testPos = new Vector2Int(
                        preferredPos.x + dx, 
                        preferredPos.y + dy
                    );

                    // Verificar lÃ­mites del grid
                    if (testPos.x >= 0 && testPos.x < gridSize && 
                        testPos.y >= 0 && testPos.y < gridSize)
                    {
                        if (GridManager.Instance.IsCellWalkable(testPos))
                        {
                            if (showDebugInfo)
                            {
                                Debug.Log($"[RobotSpawner] PosiciÃ³n alternativa encontrada: {testPos}");
                            }
                            return testPos;
                        }
                    }
                }
            }
            searchRadius++;
        }

        // Si todo falla, buscar cualquier posiciÃ³n vÃ¡lida
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                Vector2Int fallbackPos = new Vector2Int(x, y);
                if (GridManager.Instance.IsCellWalkable(fallbackPos))
                {
                    Debug.LogWarning($"[RobotSpawner] Usando posiciÃ³n de emergencia: {fallbackPos}");
                    return fallbackPos;
                }
            }
        }

        Debug.LogError("[RobotSpawner] Â¡No se encontrÃ³ ninguna posiciÃ³n vÃ¡lida!");
        return preferredPos; // Retornar la original como Ãºltimo recurso
    }

    void CreateRobot(Vector3 worldPos, Transform parent, int robotIndex, Vector2Int gridPos)
    {
        // Crear instancia del robot
        GameObject robot = Instantiate(robotPrefab, worldPos, Quaternion.identity, parent);
        robot.transform.localScale = Vector3.one * robotScale;
        robot.name = $"Robot_{robotIndex}_Color_{GetColorName(robotIndex)}";

        // *** CONFIGURACIÃ“N CRÃTICA: Obtener componente existente, NO aÃ±adir uno nuevo ***
        var gridAI = robot.GetComponent<GridBasedRobotAI>();
        if (gridAI == null)
        {
            Debug.LogError($"[RobotSpawner] Â¡El prefab del Robot {robotIndex} no tiene GridBasedRobotAI!", robot);
            return;
        }

        // Configurar las propiedades de la IA
        gridAI.targetColor = robotColors[robotIndex];
        gridAI.robotID = robotIndex;
        gridAI.plane = plane;
        gridAI.moveSpeed = robotMoveSpeed;
        gridAI.cellDelay = cellDelay;

        // Colorear el robot para identificaciÃ³n visual
        ColorizeRobot(robot, robotColors[robotIndex]);

        if (showDebugInfo)
        {
            Debug.Log($"[RobotSpawner] Robot {robotIndex} ({GetColorName(robotIndex)}) creado en Grid {gridPos} -> World {worldPos}");
        }
    }

    void ColorizeRobot(GameObject robot, Color color)
    {
        // Buscar renderers en el robot y sus hijos
        var renderers = robot.GetComponentsInChildren<Renderer>();
        
        foreach (var renderer in renderers)
        {
            // Crear nuevo material para no afectar el prefab original
            if (renderer.material != null)
            {
                var newMaterial = new Material(renderer.material);
                newMaterial.color = color;
                renderer.material = newMaterial;
            }
        }
    }

    void ClearExistingRobots()
    {
        var existingHolder = GameObject.Find("RobotsHolder");
        if (existingHolder != null) 
        {
            if (Application.isPlaying)
                Destroy(existingHolder);
            else
                DestroyImmediate(existingHolder);
        }
    }

    string GetColorName(int index)
    {
        return index switch
        {
            0 => "Rojo",
            1 => "Azul",
            2 => "Verde",
            _ => $"Color{index}"
        };
    }

    // MÃ©todo de utilidad para debugging
    [ContextMenu("Test Spawn Positions")]
    void TestSpawnPositions()
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[RobotSpawner] GridManager no estÃ¡ disponible");
            return;
        }

        int gridSize = GridManager.Instance.gridSize;
        Vector2Int[] testPositions = {
            new Vector2Int(0, 0),
            new Vector2Int(gridSize - 1, 0),
            new Vector2Int(0, gridSize - 1)
        };

        for (int i = 0; i < testPositions.Length; i++)
        {
            Vector2Int pos = testPositions[i];
            bool isWalkable = GridManager.Instance.IsCellWalkable(pos);
            Debug.Log($"[RobotSpawner] PosiciÃ³n {i}: {pos} -> {(isWalkable ? "VÃLIDA" : "BLOQUEADA")}");
            
            if (!isWalkable)
            {
                Vector2Int alternative = FindValidSpawnPosition(pos, gridSize);
                Debug.Log($"[RobotSpawner] Alternativa encontrada: {alternative}");
            }
        }
    }
}