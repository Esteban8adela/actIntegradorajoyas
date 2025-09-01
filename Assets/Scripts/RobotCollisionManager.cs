using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class RobotCollisionManager : MonoBehaviour
{
    [Header("Configuración de Colisión")]
    [Range(0.8f, 3f)] public float collisionDetectionRadius = 1.2f;
    [Range(0.5f, 2f)] public float safeDistance = 0.9f;
    [Range(0.1f, 2f)] public float waitTimeAfterCollision = 0.5f;
    
    [Header("Sistema de Prioridad")]
    [Tooltip("Robot con ID menor tiene prioridad de paso")]
    public bool lowerIDHasPriority = true;
    [Tooltip("Robot cargando gema tiene prioridad")]
    public bool carryingGemHasPriority = true;

    [Header("Debug")]
    public bool showDebugInfo = false;
    public bool showCollisionGizmos = false;
    public LayerMask robotLayer = -1; // Todos los layers por defecto

    private Dictionary<int, GridBasedRobotAI> robots = new Dictionary<int, GridBasedRobotAI>();
    private Dictionary<int, float> robotWaitTimes = new Dictionary<int, float>();
    private Dictionary<int, Vector3> lastKnownPositions = new Dictionary<int, Vector3>();
    private bool isInitialized = false;

    void Start()
    {
        // Esperar a que los robots se inicialicen
        Invoke(nameof(InitializeCollisionSystem), 0.4f);
    }

    void InitializeCollisionSystem()
    {
        // Encontrar todos los robots en la escena
        var foundRobots = FindObjectsOfType<GridBasedRobotAI>();
        
        robots.Clear();
        robotWaitTimes.Clear();
        lastKnownPositions.Clear();

        foreach (var robot in foundRobots)
        {
            robots[robot.robotID] = robot;
            robotWaitTimes[robot.robotID] = 0f;
            lastKnownPositions[robot.robotID] = robot.transform.position;

            // Asegurar que el robot tenga un collider
            EnsureRobotHasCollider(robot);
        }

        isInitialized = true;

        if (showDebugInfo)
        {
            Debug.Log($"[RobotCollisionManager] Sistema inicializado con {robots.Count} robots");
        }
    }

    void EnsureRobotHasCollider(GridBasedRobotAI robot)
    {
        var collider = robot.GetComponent<Collider>();
        if (collider == null)
        {
            // Agregar CapsuleCollider como collider por defecto
            var capsule = robot.gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 1.5f;
            capsule.radius = 0.4f;
            capsule.center = new Vector3(0, 0.75f, 0);
            
            // Hacer que sea trigger para no interferir con el movimiento
            capsule.isTrigger = true;

            if (showDebugInfo)
            {
                Debug.Log($"[RobotCollisionManager] Collider agregado a Robot {robot.robotID}");
            }
        }
        else
        {
            // Asegurar que sea trigger
            collider.isTrigger = true;
        }
    }

    void Update()
    {
        if (!isInitialized) return;

        // Actualizar tiempos de espera
        UpdateWaitTimes();

        // Detectar y manejar colisiones
        HandleCollisionDetection();

        // Actualizar posiciones conocidas
        UpdateLastKnownPositions();
    }

    void UpdateWaitTimes()
    {
        var keysToUpdate = robotWaitTimes.Keys.ToList();
        foreach (int robotID in keysToUpdate)
        {
            if (robotWaitTimes[robotID] > 0)
            {
                robotWaitTimes[robotID] -= Time.deltaTime;
                if (robotWaitTimes[robotID] <= 0)
                {
                    robotWaitTimes[robotID] = 0;
                    if (showDebugInfo)
                    {
                        Debug.Log($"[CollisionManager] Robot {robotID} puede moverse nuevamente");
                    }
                }
            }
        }
    }

    void HandleCollisionDetection()
    {
        var robotList = robots.Values.ToList();
        
        for (int i = 0; i < robotList.Count; i++)
        {
            for (int j = i + 1; j < robotList.Count; j++)
            {
                var robotA = robotList[i];
                var robotB = robotList[j];
                
                if (robotA == null || robotB == null) continue;

                CheckCollisionBetweenRobots(robotA, robotB);
            }
        }
    }

    void CheckCollisionBetweenRobots(GridBasedRobotAI robotA, GridBasedRobotAI robotB)
    {
        float distance = Vector3.Distance(robotA.transform.position, robotB.transform.position);
        
        if (distance <= collisionDetectionRadius)
        {
            // Detectar si están moviéndose uno hacia el otro
            if (AreRobotsMovingTowardsEachOther(robotA, robotB))
            {
                HandleCollision(robotA, robotB, distance);
            }
        }
    }

    bool AreRobotsMovingTowardsEachOther(GridBasedRobotAI robotA, GridBasedRobotAI robotB)
    {
        Vector3 posA = robotA.transform.position;
        Vector3 posB = robotB.transform.position;
        
        Vector3 lastPosA = lastKnownPositions.ContainsKey(robotA.robotID) ? 
            lastKnownPositions[robotA.robotID] : posA;
        Vector3 lastPosB = lastKnownPositions.ContainsKey(robotB.robotID) ? 
            lastKnownPositions[robotB.robotID] : posB;

        // Calcular direcciones de movimiento
        Vector3 dirA = (posA - lastPosA).normalized;
        Vector3 dirB = (posB - lastPosB).normalized;
        
        // Vector entre robots
        Vector3 betweenRobots = (posB - posA).normalized;
        
        // Verificar si A se mueve hacia B y B se mueve hacia A
        float dotA = Vector3.Dot(dirA, betweenRobots);
        float dotB = Vector3.Dot(dirB, -betweenRobots);
        
        return dotA > 0.5f && dotB > 0.5f; // Ambos moviéndose uno hacia el otro
    }

    void HandleCollision(GridBasedRobotAI robotA, GridBasedRobotAI robotB, float distance)
    {
        if (distance > safeDistance)
        {
            return; // Aún no es necesario actuar
        }

        // Determinar quién tiene prioridad
        GridBasedRobotAI priorityRobot = DeterminePriorityRobot(robotA, robotB);
        GridBasedRobotAI waitingRobot = (priorityRobot == robotA) ? robotB : robotA;

        // El robot sin prioridad debe esperar
        if (!IsRobotWaiting(waitingRobot.robotID))
        {
            SetRobotWaiting(waitingRobot.robotID);
            
            if (showDebugInfo)
            {
                Debug.Log($"[CollisionManager] Robot {waitingRobot.robotID} esperando por Robot {priorityRobot.robotID}");
            }
        }
    }

    GridBasedRobotAI DeterminePriorityRobot(GridBasedRobotAI robotA, GridBasedRobotAI robotB)
    {
        // Regla 1: Robot cargando gema tiene prioridad
        if (carryingGemHasPriority)
        {
            bool aHasGem = robotA.CarriedGem != null;
            bool bHasGem = robotB.CarriedGem != null;
            
            if (aHasGem && !bHasGem) return robotA;
            if (bHasGem && !aHasGem) return robotB;
        }

        // Regla 2: Robot con ID menor tiene prioridad
        if (lowerIDHasPriority)
        {
            return (robotA.robotID < robotB.robotID) ? robotA : robotB;
        }
        else
        {
            return (robotA.robotID > robotB.robotID) ? robotA : robotB;
        }
    }

    void SetRobotWaiting(int robotID)
    {
        robotWaitTimes[robotID] = waitTimeAfterCollision;
    }

    void UpdateLastKnownPositions()
    {
        foreach (var robotPair in robots)
        {
            if (robotPair.Value != null)
            {
                lastKnownPositions[robotPair.Key] = robotPair.Value.transform.position;
            }
        }
    }

    // Método público para que otros scripts consulten si un robot puede moverse
    public bool CanRobotMove(int robotID)
    {
        if (!isInitialized) return true; // Si no está inicializado, permitir movimiento
        
        return !IsRobotWaiting(robotID);
    }

    bool IsRobotWaiting(int robotID)
    {
        return robotWaitTimes.ContainsKey(robotID) && robotWaitTimes[robotID] > 0;
    }

    // Método para forzar parada de un robot (uso externo)
    public void ForceRobotWait(int robotID, float waitTime = -1)
    {
        if (waitTime < 0) waitTime = waitTimeAfterCollision;
        
        if (robotWaitTimes.ContainsKey(robotID))
        {
            robotWaitTimes[robotID] = waitTime;
            if (showDebugInfo)
            {
                Debug.Log($"[CollisionManager] Robot {robotID} forzado a esperar {waitTime:F1}s");
            }
        }
    }

    // Método para obtener información del robot más cercano
    public GridBasedRobotAI GetNearestRobot(Vector3 position, int excludeRobotID = -1)
    {
        GridBasedRobotAI nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (var robotPair in robots)
        {
            if (robotPair.Key == excludeRobotID) continue;
            if (robotPair.Value == null) continue;

            float distance = Vector3.Distance(position, robotPair.Value.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = robotPair.Value;
            }
        }

        return nearest;
    }

    // Información de debug
    public void GetCollisionSystemInfo()
    {
        Debug.Log("=== Sistema de Colisiones ===");
        Debug.Log($"Robots registrados: {robots.Count}");
        Debug.Log($"Radio de detección: {collisionDetectionRadius}");
        Debug.Log($"Distancia segura: {safeDistance}");
        
        int waitingRobots = robotWaitTimes.Count(kvp => kvp.Value > 0);
        Debug.Log($"Robots esperando: {waitingRobots}");
        
        foreach (var waitPair in robotWaitTimes.Where(kvp => kvp.Value > 0))
        {
            Debug.Log($"  - Robot {waitPair.Key}: {waitPair.Value:F1}s restantes");
        }
    }

    [ContextMenu("Show Collision Info")]
    void ShowCollisionInfo()
    {
        GetCollisionSystemInfo();
    }

    [ContextMenu("Force All Robots Wait")]
    void ForceAllRobotsWait()
    {
        foreach (var robotID in robots.Keys)
        {
            ForceRobotWait(robotID, 2f);
        }
        Debug.Log("[CollisionManager] Todos los robots forzados a esperar");
    }

    void OnDrawGizmos()
    {
        if (!showCollisionGizmos || !isInitialized) return;

        foreach (var robotPair in robots)
        {
            var robot = robotPair.Value;
            if (robot == null) continue;

            Vector3 robotPos = robot.transform.position;
            
            // Dibujar radio de detección
            Gizmos.color = IsRobotWaiting(robot.robotID) ? Color.red : Color.yellow;
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.3f);
            Gizmos.DrawSphere(robotPos, collisionDetectionRadius);
            
            // Dibujar distancia segura
            Gizmos.color = Color.green;
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.2f);
            Gizmos.DrawSphere(robotPos, safeDistance);
            
            // Indicar si está esperando
            if (IsRobotWaiting(robot.robotID))
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(robotPos + Vector3.up * 2f, Vector3.one * 0.5f);
            }
        }
    }
}