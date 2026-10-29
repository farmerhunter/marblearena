using UnityEngine;
using UnityEngine.Rendering;

namespace MarbleArena
{
    public static class Visuals
    {
        public static Color Parse(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color value) ? value : Color.white;
        }

        public static Material Material(string name, Color color, float metallic = 0, float smoothness = .4f, float emission = 0)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emission > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            material.enableInstancing = true;
            return material;
        }

        public static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>();
            collider.enabled = false;
            if(Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        public static Mesh RingMesh(float radius, float tube, int segments = 64, int sides = 8, float arc = 1)
        {
            Vector3[] vertices = new Vector3[(segments + 1) * (sides + 1)];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[segments * sides * 6];
            int t = 0;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2 * arc;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                for (int j = 0; j <= sides; j++)
                {
                    float a = j / (float)sides * Mathf.PI * 2;
                    int k = i * (sides + 1) + j;
                    normals[k] = radial * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                    vertices[k] = radial * radius + normals[k] * tube;
                    if (i == segments || j == sides) continue;
                    triangles[t++] = k; triangles[t++] = k + 1; triangles[t++] = k + sides + 1;
                    triangles[t++] = k + 1; triangles[t++] = k + sides + 2; triangles[t++] = k + sides + 1;
                }
            }
            var mesh = new Mesh { name = "Jewellery ring", vertices = vertices, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject MeshObject(string name, Mesh mesh, Transform parent, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
