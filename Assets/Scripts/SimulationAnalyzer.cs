using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SimulationAnalyzer : MonoBehaviour
{
    [Header("ConfiguraciÃ³n de AnÃ¡lisis")]
    public float maxSimulationTime = 300f;
    public bool showRealTimeStats = true;
    [Range(50f, 100f)] public float successThreshold = 90f;

    [Header("Debug")]
    public bool showDetailedLogs = false;

    private float startTime;
    private int totalGemsCollected = 0;
    private int totalRobotMoves = 0;
    private Dictionary<int, RobotStats> robotStats = new Dictionary<int, RobotStats>();
    
    private bool simulationComplete = false;
    private bool simulationSuccessful = false;
    private bool isInitialized = false;
    
    private GridBasedRobotAI[] robots;
    private int initialGemCount;

    [System.Serializable]
    public class RobotStats
    {
        public int robotID;
        public Color robotColor;
        public int gemsCollected;
        public int totalMoves;
        public GridRobotState currentState;
        public Vector2Int currentPosition;
        public bool hasGem;

        public override string ToString()
        {
            return $"Robot {robotID}: {gemsCollected} gemas, {totalMoves} movs, Estado: {currentState}";
        }
    }

    void Update()
    {
        if (!isInitialized)
        {
            TryInitialize();
            return;
        }
        
        if (simulationComplete) return;

        UpdateAllRobotStats();
        CheckSimulationCompletion();
    }

    void TryInitialize()
    {
        // Verificar que GridManager estÃ© listo
        if (GridManager.Instance == null)
        {
            if (showDetailedLogs)
                Debug.Log("[SimulationAnalyzer] Esperando GridManager...");
            return;
        }

        // Verificar que haya robots en la escena
        var availableRobots = FindObjectsOfType<GridBasedRobotAI>();
        if (availableRobots.Length == 0)
        {
            if (showDetailedLogs)
                Debug.Log("[SimulationAnalyzer] Esperando robots...");
            return;
        }

        // Verificar que el contenedor de gemas exista
        GameObject gemsHolder = GameObject.Find("GemsHolder");
        if (gemsHolder == null)
        {
            if (showDetailedLogs)
                Debug.Log("[SimulationAnalyzer] Esperando GemsHolder...");
            return;
        }

        // Todo listo, inicializar
        InitializeAnalysis();
    }

    void InitializeAnalysis()
    {
        startTime = Time.time;
        robots = FindObjectsOfType<GridBasedRobotAI>();
        
        robotStats.Clear();
        
        // Inicializar estadÃ­sticas de cada robot
        foreach (var robot in robots)
        {
            robotStats[robot.robotID] = new RobotStats
            {
                robotID = robot.robotID,
                robotColor = robot.targetColor,
                gemsCollected = 0,
                totalMoves = 0,
                currentState = robot.CurrentState, // *** USANDO PROPIEDAD PÃšBLICA ***
                currentPosition = robot.CurrentGridPos, // *** USANDO PROPIEDAD PÃšBLICA ***
                hasGem = robot.CarriedGem != null // *** USANDO PROPIEDAD PÃšBLICA ***
            };
        }
        
        // Contar gemas iniciales
        initialGemCount = CountActiveGems();
        isInitialized = true;
        
        Debug.Log($"[SimulationAnalyzer] âœ“ AnÃ¡lisis iniciado correctamente:");
        Debug.Log($"  - Robots: {robots.Length}");
        Debug.Log($"  - Gemas iniciales: {initialGemCount}");
        Debug.Log($"  - Tiempo mÃ¡ximo: {maxSimulationTime}s");
        Debug.Log($"  - Umbral de Ã©xito: {successThreshold}%");
    }

    void UpdateAllRobotStats()
    {
        if (robots == null) return;
        
        totalRobotMoves = 0;
        
        foreach (var robot in robots)
        {
            if (robot == null || !robotStats.ContainsKey(robot.robotID)) continue;
            
            var stats = robotStats[robot.robotID];
            
            // *** USANDO PROPIEDADES PÃšBLICAS EN LUGAR DE CAMPOS PRIVADOS ***
            stats.totalMoves = robot.TotalMoves;
            stats.gemsCollected = robot.GemsDelivered;
            stats.currentState = robot.CurrentState;
            stats.currentPosition = robot.CurrentGridPos;
            stats.hasGem = robot.CarriedGem != null;
            
            totalRobotMoves += stats.totalMoves;
        }
    }

    void CheckSimulationCompletion()
    {
        float elapsedTime = Time.time - startTime;
        int remainingGems = CountActiveGems();
        totalGemsCollected = initialGemCount - remainingGems;
        
        bool timeExpired = elapsedTime >= maxSimulationTime;
        bool allGemsCollected = initialGemCount > 0 && remainingGems <= 0;
        
        // Verificar condiciones de finalizaciÃ³n
        if (timeExpired || allGemsCollected)
        {
            float successPercentage = (initialGemCount > 0) ? 
                (totalGemsCollected / (float)initialGemCount) * 100f : 0f;
            
            simulationSuccessful = successPercentage >= successThreshold && allGemsCollected;
            CompleteSimulation(elapsedTime, timeExpired);
        }
    }

    int CountActiveGems()
    {
        // Contar gemas que estÃ¡n en el layer 9 (no entregadas)
        return GameObject.FindGameObjectsWithTag("Gem")
            .Count(g => g != null && g.layer == 9);
    }

    void CompleteSimulation(float elapsedTime, bool timeExpired)
    {
        if (simulationComplete) return;
        simulationComplete = true;
        
        UpdateAllRobotStats(); // ActualizaciÃ³n final
        
        // Mostrar resultados detallados
        Debug.Log("=".PadRight(60, '='));
        Debug.Log("SIMULACIÃ“N COMPLETADA");
        Debug.Log("=".PadRight(60, '='));
        Debug.Log($"Tiempo total: {elapsedTime:F2}s / {maxSimulationTime:F0}s");
        Debug.Log($"Gemas recolectadas: {totalGemsCollected}/{initialGemCount} ({(totalGemsCollected/(float)initialGemCount)*100:F1}%)");
        Debug.Log($"Movimientos totales: {totalRobotMoves}");
        Debug.Log($"RazÃ³n de finalizaciÃ³n: {(timeExpired ? "TIEMPO AGOTADO" : "TODAS LAS GEMAS RECOLECTADAS")}");
        Debug.Log($"Resultado: {(simulationSuccessful ? "Ã‰XITO âœ“" : "FALLO âœ—")}");
        
        // EstadÃ­sticas por robot
        Debug.Log("\n--- EstadÃ­sticas por Robot ---");
        foreach (var stats in robotStats.Values.OrderBy(s => s.robotID))
        {
            Debug.Log($"Robot {stats.robotID} ({GetColorName(stats.robotColor)}): " +
                     $"{stats.gemsCollected} gemas, {stats.totalMoves} movimientos, " +
                     $"Estado final: {stats.currentState}");
        }
        
        Debug.Log("=".PadRight(60, '='));
        
        // Calcular mÃ©tricas adicionales
        CalculatePerformanceMetrics(elapsedTime);
    }

    void CalculatePerformanceMetrics(float elapsedTime)
    {
        if (totalGemsCollected == 0) return;
        
        float gemsPerSecond = totalGemsCollected / elapsedTime;
        float movesPerGem = (float)totalRobotMoves / totalGemsCollected;
        float efficiency = (totalGemsCollected / (float)initialGemCount) / (elapsedTime / maxSimulationTime);
        
        Debug.Log("\n--- MÃ©tricas de Rendimiento ---");
        Debug.Log($"Gemas por segundo: {gemsPerSecond:F2}");
        Debug.Log($"Movimientos por gema: {movesPerGem:F1}");
        Debug.Log($"Ãndice de eficiencia: {efficiency:F2}");
    }

    string GetColorName(Color color)
    {
        // Identificar color basado en valores RGB
        if (color.r > 0.8f && color.g < 0.3f && color.b < 0.3f) return "Rojo";
        if (color.r < 0.3f && color.g > 0.4f && color.b > 0.8f) return "Azul";
        if (color.r < 0.4f && color.g > 0.7f && color.b < 0.4f) return "Verde";
        return "Desconocido";
    }

    void OnGUI()
    {
        if (!showRealTimeStats || !isInitialized) return;

        float elapsedTime = Time.time - startTime;
        float progress = (initialGemCount > 0) ? ((float)totalGemsCollected / initialGemCount) * 100f : 0f;
        int remainingGems = CountActiveGems();

        // Panel principal
        float panelWidth = 300f;
        float panelHeight = 120 + (robots.Length * 22);
        GUI.Box(new Rect(10, 10, panelWidth, panelHeight), "AnÃ¡lisis de SimulaciÃ³n");
        
        // InformaciÃ³n general
        string mainInfo = $"Tiempo: {elapsedTime:F1}s / {maxSimulationTime:F0}s\n" +
                         $"Progreso: {progress:F1}% ({totalGemsCollected}/{initialGemCount})\n" +
                         $"Gemas restantes: {remainingGems}\n" +
                         $"Movimientos totales: {totalRobotMoves}\n" +
                         $"Estado: {GetSimulationStatusText()}";
        
        GUI.Label(new Rect(15, 30, panelWidth - 10, 100), mainInfo);

        // InformaciÃ³n por robot
        float yOffset = 130;
        foreach (var stats in robotStats.Values.OrderBy(s => s.robotID))
        {
            string robotInfo = $"Robot {stats.robotID} ({GetColorName(stats.robotColor)}): " +
                              $"{stats.gemsCollected} gemas, {stats.totalMoves} movs, " +
                              $"{stats.currentState}" +
                              $"{(stats.hasGem ? " [Cargando]" : "")}";
            
            GUI.Label(new Rect(15, yOffset, panelWidth - 10, 20), robotInfo);
            yOffset += 22;
        }
    }

    string GetSimulationStatusText()
    {
        if (simulationComplete)
        {
            return simulationSuccessful ? "COMPLETADO CON Ã‰XITO" : "TERMINADO SIN Ã‰XITO";
        }
        return "EN PROGRESO...";
    }

    // MÃ©todos pÃºblicos para acceso externo
    public bool IsSimulationComplete() => simulationComplete;
    public bool IsSimulationSuccessful() => simulationSuccessful;
    public float GetElapsedTime() => isInitialized ? Time.time - startTime : 0f;
    public int GetTotalGemsCollected() => totalGemsCollected;
    public int GetTotalMoves() => totalRobotMoves;
    public Dictionary<int, RobotStats> GetRobotStats() => new Dictionary<int, RobotStats>(robotStats);

    // MÃ©todo de utilidad para debugging
    [ContextMenu("Show Current Stats")]
    void ShowCurrentStats()
    {
        if (!isInitialized)
        {
            Debug.Log("[SimulationAnalyzer] SimulaciÃ³n no inicializada");
            return;
        }

        Debug.Log($"=== Estado Actual de la SimulaciÃ³n ===");
        Debug.Log($"Tiempo transcurrido: {GetElapsedTime():F2}s");
        Debug.Log($"Gemas recolectadas: {totalGemsCollected}/{initialGemCount}");
        Debug.Log($"Movimientos totales: {totalRobotMoves}");
        Debug.Log($"Estado: {GetSimulationStatusText()}");
        
        foreach (var stats in robotStats.Values)
        {
            Debug.Log(stats.ToString());
        }
    }
}