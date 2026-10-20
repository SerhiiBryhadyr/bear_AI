using System.Collections.Generic;
using UnityEngine;

// ============================================================
// Читання terrain: висота, нахил, дерева Paint Trees, шари текстур.
// Працює з кількома terrain одразу — шукає той, над яким точка.
// ============================================================
public static class ScatterTerrain
{
    // ===== Terrain під точкою (за X і Z) =====
    public static Terrain FindTerrain(Vector3 world)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            Vector3 p = t.transform.position;
            Vector3 s = t.terrainData.size;
            if (world.x >= p.x && world.x <= p.x + s.x && world.z >= p.z && world.z <= p.z + s.z)
                return t;
        }
        return null;
    }

    // ===== Висота землі і її нормаль (куди «дивиться» схил) =====
    public static bool SampleGround(Vector3 world, out float height, out Vector3 normal)
    {
        height = 0f;
        normal = Vector3.up;
        Terrain t = FindTerrain(world);
        if (t == null) return false;

        height = t.SampleHeight(world) + t.transform.position.y;
        Vector3 local = world - t.transform.position;
        Vector3 s = t.terrainData.size;
        normal = t.terrainData.GetInterpolatedNormal(local.x / s.x, local.z / s.z);
        return true;
    }

    // ===== Стовбури дерев Paint Trees у прямокутнику (світові координати) =====
    public static List<Vector3> GetTreesInBounds(Bounds area)
    {
        var result = new List<Vector3>();
        foreach (Terrain t in Terrain.activeTerrains)
        {
            TerrainData d = t.terrainData;
            Vector3 p = t.transform.position;
            foreach (TreeInstance tree in d.treeInstances)
            {
                Vector3 w = Vector3.Scale(tree.position, d.size) + p; // позиція дерева зберігається як 0..1
                if (w.x >= area.min.x && w.x <= area.max.x && w.z >= area.min.z && w.z <= area.max.z)
                    result.Add(w);
            }
        }
        return result;
    }

    // ===== Наскільки в точці намальований шар текстури (0 — немає, 1 — тільки він) =====
    public static float GetLayerWeight(Vector3 world, TerrainLayer layer)
    {
        Terrain t = FindTerrain(world);
        if (t == null || layer == null) return 0f;
        TerrainData d = t.terrainData;
        int index = System.Array.IndexOf(d.terrainLayers, layer);
        if (index < 0) return 0f;

        Vector3 local = world - t.transform.position;
        int x = Mathf.Clamp((int)(local.x / d.size.x * d.alphamapWidth), 0, d.alphamapWidth - 1);
        int y = Mathf.Clamp((int)(local.z / d.size.z * d.alphamapHeight), 0, d.alphamapHeight - 1);
        float[,,] map = d.GetAlphamaps(x, y, 1, 1);
        return map[0, 0, index];
    }
}
