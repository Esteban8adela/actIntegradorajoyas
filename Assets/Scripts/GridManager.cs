using UnityEngine;
using System.Collections.Generic;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("ConfiguraciÃ³n del Grid")]
    public int gridSize = 19;
    public Transform plane;
    public LayerMask wallLayer;

    private HashSet<Vector2Int> wallCells = new HashSet<Vector2Int>();
    private Vector3 planeCenter;
    private float cellWidth;
    private float cellHeight;

    void Awake()
    {
        if (Instance != null && Instance != this) 
        { 
            Destroy(gameObject); 
        }
        else 
        { 
            Instance = this; 
        }
        
        InitializeGridParameters();
        ScanForWalls();
    }

    private void InitializeGridParameters()
    {
        if (plane == null) plane = GameObject.Find("Plane")?.transform;
        if (plane == null) 
        { 
            Debug.LogError("[GridManager] Â¡No se encontrÃ³ el plano!", this); 
            return; 
        }

        Vector3 worldScale = plane.lossyScale;
        planeCenter = plane.position;
        cellWidth = (10f * worldScale.x) / gridSize;
        cellHeight = (10f * worldScale.z) / gridSize;
    }

    private void ScanForWalls()
    {
        wallCells.Clear();
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                Vector3 cellCenter = GridToWorld(new Vector2Int(x, y));
                Vector3 checkHalfExtents = new Vector3(cellWidth * 0.45f, 1f, cellHeight * 0.45f);

                if (Physics.CheckBox(cellCenter, checkHalfExtents, Quaternion.identity, wallLayer))
                {
                    wallCells.Add(new Vector2Int(x, y));
                }
            }
        }
        Debug.Log($"[GridManager] Escaneo completado. Se mapearon {wallCells.Count} celdas de pared.");
    }

    public bool IsCellWalkable(Vector2Int gridPos)
    {
        if (gridPos.x < 0 || gridPos.x >= gridSize || gridPos.y < 0 || gridPos.y >= gridSize)
        {
            return false;
        }
        return !wallCells.Contains(gridPos);
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        float offsetX = (gridPos.x - (gridSize - 1) * 0.5f) * cellWidth;
        float offsetZ = (gridPos.y - (gridSize - 1) * 0.5f) * cellHeight;
        return new Vector3(planeCenter.x + offsetX, plane.position.y + 0.5f, planeCenter.z + offsetZ);
    }

    // *** MÃ‰TODO AÃ‘ADIDO PARA COMPATIBILIDAD ***
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
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

    // *** MÃ‰TODOS DE UTILIDAD ADICIONALES ***
    public float GetCellWidth() => cellWidth;
    public float GetCellHeight() => cellHeight;
    public Vector3 GetPlaneCenter() => planeCenter;

    // MÃ©todo para obtener una posiciÃ³n caminable cercana
    public Vector2Int FindNearestWalkablePosition(Vector2Int startPos)
    {
        if (IsCellWalkable(startPos)) return startPos;

        // Buscar en espiral
        for (int radius = 1; radius < gridSize / 2; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Solo verificar el borde del cuadrado de bÃºsqueda
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius)
                        continue;

                    Vector2Int testPos = new Vector2Int(startPos.x + dx, startPos.y + dy);
                    
                    if (IsCellWalkable(testPos))
                    {
                        return testPos;
                    }
                }
            }
        }

        return startPos; // Fallback
    }
}