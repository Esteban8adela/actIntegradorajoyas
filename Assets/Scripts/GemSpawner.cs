using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GemSpawner : MonoBehaviour
{
    [Header("Escena / Referencias")]
    public Transform plane;
    public GameObject gemPrefab;

    [Header("Materiales (Arrastra aquÃ­ los 3 materiales de colores)")]
    public Material[] gemMaterials; // Rojo, Azul, Verde

    [Header("ConfiguraciÃ³n")]
    [Min(1)] public int gemCount = 21;
    [Range(0.2f, 50f)] public float gemScale = 25f;
    [Range(0f, 1f)] public float heightOffset = 0.3f;

    [Header("Debug")]
    public bool showDebugInfo = true;

    void Start()
    {
        // PequeÃ±o delay para asegurar que GridManager estÃ© listo
        Invoke(nameof(SpawnGems), 0.1f);
    }

    [ContextMenu("Spawn Gems Now")]
    public void SpawnGems()
    {
        // Validar referencias bÃ¡sicas
        if (!ValidateReferences()) return;

        // Obtener gridSize del GridManager (fuente Ãºnica de verdad)
        int gridSize = GridManager.Instance.gridSize;

        // Limpiar gemas existentes
        ClearExistingGems();
        
        // Crear contenedor para las gemas
        var holder = new GameObject("GemsHolder");

        // Obtener posiciones vÃ¡lidas (solo celdas caminables)
        var availablePositions = GetWalkablePositions(gridSize);
        
        if (availablePositions.Count == 0)
        {
            Debug.LogError("[GemSpawner] Â¡No hay posiciones vÃ¡lidas para spawner gemas!");
            return;
        }

        // Seleccionar posiciones aleatorias
        var selectedPositions = availablePositions
            .OrderBy(p => Random.value)
            .Take(Mathf.Min(gemCount, availablePositions.Count))
            .ToList();

        // Crear las gemas
        for (int i = 0; i < selectedPositions.Count; i++)
        {
            Vector2Int gridPos = selectedPositions[i];
            Vector3 worldPos = GridManager.Instance.GridToWorld(gridPos);
            worldPos.y += heightOffset; // Ajustar altura
            
            var gem = CreateGem(worldPos, holder.transform);
            AssignGemColor(gem, i % gemMaterials.Length);
            gem.name = $"Gem_Grid[{gridPos.x},{gridPos.y}]_Color{i % gemMaterials.Length}";
        }

        if (showDebugInfo)
        {
            Debug.Log($"[GemSpawner] âœ“ Generadas {selectedPositions.Count} gemas en posiciones vÃ¡lidas.");
            Debug.Log($"[GemSpawner] Grid Size usado: {gridSize} (del GridManager)");
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
                Debug.LogError("[GemSpawner] Â¡No se encontrÃ³ el Plane en la escena!");
                return false;
            }
        }

        // Verificar GridManager
        if (GridManager.Instance == null)
        {
            Debug.LogError("[GemSpawner] Â¡GridManager no estÃ¡ inicializado!");
            return false;
        }

        // Verificar materiales
        if (gemMaterials == null || gemMaterials.Length < 3 || gemMaterials.Any(m => m == null))
        {
            Debug.LogError("[GemSpawner] Â¡Asigna los 3 materiales de gemas en el Inspector!");
            return false;
        }

        return true;
    }

    List<Vector2Int> GetWalkablePositions(int gridSize)
    {
        var positions = new List<Vector2Int>();
        
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                Vector2Int gridPos = new Vector2Int(x, z);
                
                // Solo aÃ±adir si la celda es caminable
                if (GridManager.Instance.IsCellWalkable(gridPos))
                {
                    positions.Add(gridPos);
                }
            }
        }

        return positions;
    }

    GameObject CreateGem(Vector3 position, Transform parent)
    {
        GameObject gem;
        
        // Usar el prefab si estÃ¡ disponible, sino crear una esfera
        if (gemPrefab != null)
        {
            gem = Instantiate(gemPrefab, position, Quaternion.identity, parent);
        }
        else
        {
            gem = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gem.transform.position = position;
            gem.transform.SetParent(parent);
        }

        // Configurar propiedades del objeto
        gem.transform.localScale = Vector3.one * gemScale;
        gem.layer = 9; // Layer para gemas
        gem.tag = "Gem";

        // Asegurar que tenga collider
        if (gem.GetComponent<Collider>() == null)
        {
            gem.AddComponent<SphereCollider>();
        }

        return gem;
    }

    void AssignGemColor(GameObject gem, int materialIndex)
    {
        var renderer = gem.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = gem.GetComponentInChildren<Renderer>();
        }

        if (renderer != null && materialIndex < gemMaterials.Length)
        {
            // AsignaciÃ³n directa del material (mÃ¡s confiable que MaterialPropertyBlock)
            renderer.material = gemMaterials[materialIndex];
            
            if (showDebugInfo)
            {
                string colorName = materialIndex switch
                {
                    0 => "Rojo",
                    1 => "Azul", 
                    2 => "Verde",
                    _ => $"Color{materialIndex}"
                };
                Debug.Log($"[GemSpawner] Gema pintada de {colorName}");
            }
        }
        else
        {
            Debug.LogWarning($"[GemSpawner] No se pudo asignar material a la gema {gem.name}");
        }
    }

    void ClearExistingGems()
    {
        var existingHolder = GameObject.Find("GemsHolder");
        if (existingHolder != null) 
        {
            if (Application.isPlaying)
                Destroy(existingHolder);
            else
                DestroyImmediate(existingHolder);
        }
    }

    // MÃ©todo de utilidad para debugging
    [ContextMenu("Show Grid Info")]
    void ShowGridInfo()
    {
        if (GridManager.Instance != null)
        {
            Debug.Log($"[GemSpawner] Grid Size del GridManager: {GridManager.Instance.gridSize}");
            var walkable = GetWalkablePositions(GridManager.Instance.gridSize);
            Debug.Log($"[GemSpawner] Posiciones caminables disponibles: {walkable.Count}");
        }
        else
        {
            Debug.LogError("[GemSpawner] GridManager no disponible para consulta");
        }
    }
}