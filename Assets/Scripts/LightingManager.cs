using UnityEngine;
using System.Collections.Generic;

public class LightingManager : MonoBehaviour
{
    [Header("Luz Direccional")]
    [Tooltip("Si no se asigna, se creará automáticamente")]
    public Light directionalLight;
    public Color directionalLightColor = Color.white;
    [Range(0.1f, 3f)] public float directionalIntensity = 1.2f;
    public Vector3 directionalRotation = new Vector3(50f, -30f, 0f);

    [Header("Luces de Robots (Tipo Sirena)")]
    public Color[] sirenColors = new Color[3]
    {
        Color.red,      // Robot Rojo
        Color.blue,     // Robot Azul  
        Color.green     // Robot Verde
    };
    
    [Range(0.5f, 8f)] public float sirenIntensity = 2f;
    [Range(1f, 20f)] public float sirenRange = 8f;
    [Range(0.5f, 5f)] public float sirenHeight = 1.5f;
    
    [Header("Animación de Sirena")]
    public bool enableSirenAnimation = true;
    [Range(0.5f, 3f)] public float pulseSpeed = 1.5f;
    [Range(0.3f, 1f)] public float minIntensityMultiplier = 0.4f;

    [Header("Debug")]
    public bool showDebugInfo = false;

    private Dictionary<int, Light> robotLights = new Dictionary<int, Light>();
    private GridBasedRobotAI[] robots;
    private bool isInitialized = false;

    void Start()
    {
        // Pequeño delay para que los robots se inicialicen primero
        Invoke(nameof(InitializeLighting), 0.3f);
    }

    void InitializeLighting()
    {
        SetupDirectionalLight();
        SetupRobotLights();
        isInitialized = true;

        if (showDebugInfo)
        {
            Debug.Log($"[LightingManager] Sistema de iluminación inicializado");
            Debug.Log($"[LightingManager] - Luz direccional: {(directionalLight != null ? "✓" : "✗")}");
            Debug.Log($"[LightingManager] - Luces de robots: {robotLights.Count}");
        }
    }

    void SetupDirectionalLight()
    {
        // Buscar luz direccional existente o crear una nueva
        if (directionalLight == null)
        {
            var existingDirectional = FindObjectOfType<Light>();
            if (existingDirectional != null && existingDirectional.type == LightType.Directional)
            {
                directionalLight = existingDirectional;
                if (showDebugInfo) Debug.Log("[LightingManager] Usando luz direccional existente");
            }
            else
            {
                // Crear nueva luz direccional
                GameObject lightObj = new GameObject("DirectionalLight_Main");
                directionalLight = lightObj.AddComponent<Light>();
                if (showDebugInfo) Debug.Log("[LightingManager] Creada nueva luz direccional");
            }
        }

        // Configurar propiedades
        directionalLight.type = LightType.Directional;
        directionalLight.color = directionalLightColor;
        directionalLight.intensity = directionalIntensity;
        directionalLight.shadows = LightShadows.Soft;
        directionalLight.transform.rotation = Quaternion.Euler(directionalRotation);

        // Posicionar sobre el centro de la escena
        if (GridManager.Instance != null)
        {
            Vector3 sceneCenter = GridManager.Instance.GetPlaneCenter();
            directionalLight.transform.position = sceneCenter + Vector3.up * 10f;
        }
    }

    void SetupRobotLights()
    {
        // Buscar todos los robots en la escena
        robots = FindObjectsOfType<GridBasedRobotAI>();
        
        foreach (var robot in robots)
        {
            CreateRobotLight(robot);
        }

        if (showDebugInfo && robots.Length > 0)
        {
            Debug.Log($"[LightingManager] Configuradas {robotLights.Count} luces de robot");
        }
    }

    void CreateRobotLight(GridBasedRobotAI robot)
    {
        if (robot == null) return;

        // Crear objeto para la luz
        GameObject lightObj = new GameObject($"SirenLight_Robot{robot.robotID}");
        lightObj.transform.SetParent(robot.transform);
        
        // Posicionar la luz encima del robot
        lightObj.transform.localPosition = new Vector3(0, sirenHeight, 0);
        
        // Configurar componente Light
        Light sirenLight = lightObj.AddComponent<Light>();
        sirenLight.type = LightType.Point;
        sirenLight.intensity = sirenIntensity;
        sirenLight.range = sirenRange;
        sirenLight.shadows = LightShadows.Soft;
        
        // Asignar color según el robot
        if (robot.robotID < sirenColors.Length)
        {
            sirenLight.color = sirenColors[robot.robotID];
        }
        else
        {
            sirenLight.color = robot.targetColor; // Usar color del robot como fallback
        }

        // Guardar referencia
        robotLights[robot.robotID] = sirenLight;

        if (showDebugInfo)
        {
            string colorName = robot.robotID switch
            {
                0 => "Rojo",
                1 => "Azul", 
                2 => "Verde",
                _ => $"Color{robot.robotID}"
            };
            Debug.Log($"[LightingManager] Luz sirena {colorName} creada para Robot {robot.robotID}");
        }
    }

    void Update()
    {
        if (!isInitialized) return;

        // Animar luces de sirena si está habilitado
        if (enableSirenAnimation)
        {
            AnimateSirenLights();
        }

        // Verificar si necesitamos crear luces para robots nuevos
        CheckForNewRobots();
    }

    void AnimateSirenLights()
    {
        float pulseValue = Mathf.Sin(Time.time * pulseSpeed * Mathf.PI);
        float normalizedPulse = (pulseValue + 1f) * 0.5f; // Convertir de [-1,1] a [0,1]
        
        float currentMultiplier = Mathf.Lerp(minIntensityMultiplier, 1f, normalizedPulse);

        foreach (var lightPair in robotLights)
        {
            if (lightPair.Value != null)
            {
                lightPair.Value.intensity = sirenIntensity * currentMultiplier;
            }
        }
    }

    void CheckForNewRobots()
    {
        var currentRobots = FindObjectsOfType<GridBasedRobotAI>();
        
        foreach (var robot in currentRobots)
        {
            if (!robotLights.ContainsKey(robot.robotID))
            {
                CreateRobotLight(robot);
                if (showDebugInfo)
                {
                    Debug.Log($"[LightingManager] Nueva luz creada para Robot {robot.robotID} detectado");
                }
            }
        }
    }

    // Métodos públicos para control externo
    public void SetDirectionalLightIntensity(float intensity)
    {
        if (directionalLight != null)
        {
            directionalLight.intensity = intensity;
            directionalIntensity = intensity;
        }
    }

    public void SetSirenIntensity(float intensity)
    {
        sirenIntensity = intensity;
        foreach (var light in robotLights.Values)
        {
            if (light != null)
            {
                light.intensity = intensity;
            }
        }
    }

    public void SetSirenAnimation(bool enabled)
    {
        enableSirenAnimation = enabled;
        
        if (!enabled)
        {
            // Restaurar intensidad base cuando se desactiva la animación
            foreach (var light in robotLights.Values)
            {
                if (light != null)
                {
                    light.intensity = sirenIntensity;
                }
            }
        }
    }

    public void SetSirenColor(int robotID, Color newColor)
    {
        if (robotLights.ContainsKey(robotID) && robotLights[robotID] != null)
        {
            robotLights[robotID].color = newColor;
            if (robotID < sirenColors.Length)
            {
                sirenColors[robotID] = newColor;
            }
        }
    }

    // Método para obtener información del sistema
    public void GetLightingInfo()
    {
        Debug.Log("=== Sistema de Iluminación ===");
        Debug.Log($"Luz Direccional: {(directionalLight != null ? "Activa" : "No encontrada")}");
        if (directionalLight != null)
        {
            Debug.Log($"  - Intensidad: {directionalLight.intensity}");
            Debug.Log($"  - Color: {directionalLight.color}");
            Debug.Log($"  - Rotación: {directionalLight.transform.eulerAngles}");
        }
        
        Debug.Log($"Luces de Robot: {robotLights.Count} activas");
        foreach (var lightPair in robotLights)
        {
            var light = lightPair.Value;
            if (light != null)
            {
                Debug.Log($"  - Robot {lightPair.Key}: {light.color}, Intensidad: {light.intensity:F1}");
            }
        }
        
        Debug.Log($"Animación de sirena: {(enableSirenAnimation ? "Activada" : "Desactivada")}");
    }

    // Método de contexto para debug
    [ContextMenu("Show Lighting Info")]
    void ShowLightingInfo()
    {
        GetLightingInfo();
    }

    [ContextMenu("Toggle Siren Animation")]
    void ToggleSirenAnimation()
    {
        SetSirenAnimation(!enableSirenAnimation);
        Debug.Log($"[LightingManager] Animación de sirena: {(enableSirenAnimation ? "Activada" : "Desactivada")}");
    }

    void OnDrawGizmosSelected()
    {
        if (!isInitialized) return;

        // Dibujar rango de luces de robot
        foreach (var lightPair in robotLights)
        {
            var light = lightPair.Value;
            if (light != null)
            {
                Gizmos.color = light.color * 0.3f;
                Gizmos.DrawWireSphere(light.transform.position, light.range);
                
                Gizmos.color = light.color;
                Gizmos.DrawWireCube(light.transform.position, Vector3.one * 0.2f);
            }
        }

        // Dibujar dirección de luz direccional
        if (directionalLight != null)
        {
            Gizmos.color = directionalLightColor * 0.7f;
            Vector3 lightPos = directionalLight.transform.position;
            Vector3 lightDirection = directionalLight.transform.forward;
            
            Gizmos.DrawRay(lightPos, lightDirection * 5f);
            Gizmos.DrawWireCube(lightPos, Vector3.one * 0.5f);
        }
    }
}