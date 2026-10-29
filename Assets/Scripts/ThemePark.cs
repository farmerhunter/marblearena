using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarbleArena
{
    public sealed class ThemePark : MonoBehaviour
    {
        readonly Dictionary<Material, Batch> scenery = new Dictionary<Material, Batch>();
        readonly List<Transform> cabins = new List<Transform>();
        Material cream, sand, wood, brass, coral, teal, yellow, water, hedge;
        Material[] grass, foliage, paints;
        Shape cube, sphere, cylinder;
        Transform carousel, wheel;
        bool built;

        public void Build()
        {
            if (built) return;
            built = true;
            CreateMaterials();
            cube = CubeShape();
            sphere = SphereShape();
            cylinder = CylinderShape();
            Terrain();
            Paths();
            Lake();
            Planting();
            Stalls();
            FenceAndEntrance();
            Bunting();
            FestoonBulbs();
            Carousel();
            FerrisWheel();
            Flush(scenery, transform, "Park scenery / ");
        }

        void Update()
        {
            if (carousel != null) carousel.Rotate(Vector3.up, Time.deltaTime * 5f, Space.Self);
            if (wheel == null) return;
            wheel.Rotate(Vector3.forward, Time.deltaTime * 3.5f, Space.Self);
            foreach (Transform cabin in cabins) cabin.localRotation = Quaternion.Inverse(wheel.localRotation);
        }

        void CreateMaterials()
        {
            cream = Paint("Cream yellow stage paint", "F6DDA3", 0.48f);
            sand = Paint("Warm painted promenades", "C5AE7C", 0.26f);
            wood = Paint("Dark stage timber", "5F4244", 0.3f);
            brass = Paint("Brass ride fittings", "C5A254", 0.52f, 0.45f);
            coral = Paint("Vermilion fairground paint", "E44840", 0.58f);
            teal = Paint("Cobalt fairground paint", "387BD0", 0.56f);
            yellow = Paint("Golden fairground canvas", "F4C34D", 0.46f);
            water = Paint("Blue night lagoon", "287FA7", 0.84f, 0.1f);
            hedge = Paint("Sculpted turquoise hedges", "277A76", 0.34f);
            grass = new[] { Paint("Indigo stage floor", "1D2844", 0.28f), Paint("Blue stage floor", "253A55", 0.28f), Paint("Plum stage floor", "302B4B", 0.28f) };
            foliage = new[] { Paint("Teal painted topiary", "328F86", 0.36f), Paint("Blue painted topiary", "436D9E", 0.36f), Paint("Lime painted topiary", "8A9E48", 0.36f) };
            paints = new[] { coral, teal, yellow, cream };
        }

        static Material Paint(string name, string html, float smoothness, float metallic = 0f)
        {
            ColorUtility.TryParseHtmlString("#" + html, out Color color);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            return material;
        }

        static float Ground(float x, float z)
        {
            float squareRadius = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float outer = Mathf.SmoothStep(0f, 1f, (squareRadius - 23f) / 5f);
            float height = Mathf.Lerp(-2.2f, -3.5f, outer);
            float pond = Mathf.Sqrt(Mathf.Pow((x + 34f) / 8.5f, 2f) + Mathf.Pow((z - 6f) / 6.5f, 2f));
            float pondBlend = 1f - Mathf.SmoothStep(0f, 1f, (pond - 1.05f) / 0.4f);
            return Mathf.Lerp(height, -3.55f, pondBlend);
        }

        void Terrain()
        {
            const int steps = 52;
            const float spacing = 1.8f;
            float half = steps * spacing * 0.5f;
            for (int x = 0; x < steps; x++)
            for (int z = 0; z < steps; z++)
            {
                float px = x * spacing - half;
                float pz = z * spacing - half;
                if (Mathf.Abs(px + spacing * 0.5f) < 22.4f && Mathf.Abs(pz + spacing * 0.5f) < 22.4f) continue;
                float tint = Mathf.Sin(px * 0.16f) + Mathf.Cos(pz * 0.14f);
                Material material = grass[tint > 0.7f ? 1 : tint < -0.6f ? 2 : 0];
                Quad(material, new Vector3(px, Ground(px, pz), pz),
                    new Vector3(px, Ground(px, pz + spacing), pz + spacing),
                    new Vector3(px + spacing, Ground(px + spacing, pz + spacing), pz + spacing),
                    new Vector3(px + spacing, Ground(px + spacing, pz), pz), Vector3.up);
            }
            Box(new Vector3(0f,-4.05f,0f),new Vector3(93.6f,1f,93.6f),Quaternion.identity,grass[0]);
            Box(new Vector3(0f,-2.19f,0f),new Vector3(44.9f,0.06f,44.9f),Quaternion.identity,grass[1]);
            Disc(new Vector3(34f,-3.455f,9f),8.5f,8.5f,teal,64);
            Disc(new Vector3(18f,-3.45f,-36f),13f,5.4f,coral,64);
            Disc(new Vector3(-14f,-3.445f,-35f),10f,5.1f,yellow,64);
        }

        void Paths()
        {
            List<Vector3> promenade = new List<Vector3>();
            for (int index = 0; index <= 128; index++)
            {
                float a = index / 128f * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float denominator = Mathf.Pow(c * c * c * c + s * s * s * s, 0.25f);
                float x = c * 28f / denominator;
                float z = s * 28f / denominator;
                promenade.Add(new Vector3(x, Ground(x, z) + 0.055f, z));
            }
            Ribbon(promenade, 3.4f, sand);
            List<Vector3> entrance = new List<Vector3>();
            for (int index = 0; index <= 30; index++)
            {
                float z = -55f + index;
                entrance.Add(new Vector3(0f, Ground(0f, z) + 0.065f, z));
            }
            Ribbon(entrance, 6.2f, sand);
            for (int side = 0; side < 4; side++)
            {
                Quaternion turn = Quaternion.Euler(0f, side * 90f, 0f);
                List<Vector3> inner = new List<Vector3>();
                for (int index = 0; index <= 21; index++)
                {
                    Vector3 point = turn * new Vector3(0f, 0f, 6.5f + index);
                    point.y = Ground(point.x, point.z) + 0.07f;
                    inner.Add(point);
                }
                Ribbon(inner, 2.25f, sand);
            }
            Disc(new Vector3(0f, -2.105f, 0f), 6.5f, 6.5f, cream, 64);
            for (int index = 0; index < 32; index++)
            {
                float a = index / 32f * Mathf.PI * 2f;
                Vector3 point = new Vector3(Mathf.Cos(a) * 6.15f, -2.08f, Mathf.Sin(a) * 6.15f);
                Box(point, new Vector3(0.32f, 0.035f, 0.12f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), wood);
            }
            for (int index = 0; index < 14; index++)
            {
                float x = 8f + index * 2f;
                float z = -33.5f;
                Box(new Vector3(x, Ground(x, z) + 0.055f, z), new Vector3(1.7f, 0.07f, 4f), Quaternion.identity, sand);
            }
        }

        void Lake()
        {
            Disc(new Vector3(-34f, -3.47f, 6f), 8.7f, 6.7f, sand, 64);
            Disc(new Vector3(-34f, -3.43f, 6f), 8.2f, 6.2f, water, 64);
            for (int plank = 0; plank < 9; plank++)
                Box(new Vector3(-29.7f + plank * 0.38f, -3.15f, 1.7f), new Vector3(0.34f, 0.14f, 2.1f), Quaternion.identity, wood);
            foreach (float x in new[] { -29.5f, -26.6f })
            foreach (float z in new[] { 0.8f, 2.6f })
                Rod(new Vector3(x, -4.1f, z), new Vector3(x, -2.65f, z), 0.09f, wood);
            for (int boat = 0; boat < 2; boat++)
            {
                Vector3 center = new Vector3(-35.5f + boat * 4.2f, -3.16f, 5f + boat * 3.2f);
                Quaternion turn = Quaternion.Euler(0f, boat * 65f - 20f, 0f);
                Add(sphere, center, new Vector3(0.75f, 0.22f, 1.65f), turn, paints[boat]);
                Box(center + Vector3.up * 0.18f, new Vector3(1f, 0.1f, 0.36f), turn, cream);
                Rod(center + turn * new Vector3(-1.2f, 0.16f, -0.3f), center + turn * new Vector3(1.2f, 0.16f, 0.3f), 0.035f, wood);
            }
            for (int index = 0; index < 26; index++)
            {
                float angle = index / 26f * Mathf.PI * 2f;
                Vector3 root = new Vector3(-34f + Mathf.Cos(angle) * 8.45f, -3.4f, 6f + Mathf.Sin(angle) * 6.4f);
                for (int reed = 0; reed < 3; reed++)
                {
                    Vector3 basePoint = root + new Vector3(reed * 0.12f, 0f, Mathf.Sin(index + reed) * 0.15f);
                    Rod(basePoint, basePoint + new Vector3(0.08f, 0.45f + reed * 0.2f, 0.05f), 0.018f, hedge);
                }
            }
        }

        void Planting()
        {
            System.Random random = new System.Random(7124);
            int placed = 0;
            for (int attempt = 0; attempt < 220 && placed < 28; attempt++)
            {
                float x = (float)random.NextDouble() * 82f - 41f;
                float z = (float)random.NextDouble() * 82f - 41f;
                float squareRadius = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                if (squareRadius < 24f || squareRadius > 42f || Mathf.Abs(squareRadius - 28f) < 3f) continue;
                if (Mathf.Pow((x + 34f) / 11f, 2f) + Mathf.Pow((z - 6f) / 9f, 2f) < 1f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(34f, 9f)) < 8f) continue;
                if (z < -30f && x > -23f && x < 33f || Mathf.Abs(x) < 5f && z < -24f) continue;
                float size = 0.8f + (float)random.NextDouble() * 0.55f;
                Tree(new Vector3(x, Ground(x, z), z), size, placed);
                placed++;
            }
            for (int side = 0; side < 4; side++)
            {
                Quaternion turn = Quaternion.Euler(0f, side * 90f, 0f);
                for (int bed = 0; bed < 2; bed++)
                {
                    Vector3 center = turn * new Vector3(bed == 0 ? -12.5f : 12.5f, -2.18f, 20.8f);
                    Box(center + Vector3.up * 0.16f, new Vector3(8f, 0.32f, 1.1f), turn, cream);
                    Box(center + Vector3.up * 0.47f, new Vector3(7.75f, 0.68f, 0.9f), turn, hedge);
                    for (int flower = 0; flower < 10; flower++)
                    {
                        Vector3 point = center + turn * new Vector3(-3.4f + flower * 0.75f, 0.88f, 0f);
                        Add(sphere, point, new Vector3(0.13f, 0.12f, 0.13f), Quaternion.identity, paints[(flower + side) % paints.Length]);
                    }
                }
                Tree(turn * new Vector3(17.2f, -2.2f, 15.5f), 0.78f, side);
                Bench(turn * new Vector3(9.3f, -2.13f, 7.8f), turn);
            }
        }

        void Tree(Vector3 root, float size, int tint)
        {
            Rod(root, root + Vector3.up * (2.1f * size), 0.18f * size, wood);
            Add(sphere, root + Vector3.up * (2.8f * size), new Vector3(1.35f, 1.65f, 1.3f) * size, Quaternion.Euler(0f, tint * 43f, 0f), foliage[tint % foliage.Length]);
            Add(sphere, root + new Vector3(0.7f, 2.35f, 0.15f) * size, new Vector3(1f, 1.1f, 1f) * size, Quaternion.identity, foliage[(tint + 1) % foliage.Length]);
            Add(sphere, root + new Vector3(-0.65f, 2.4f, -0.18f) * size, new Vector3(0.9f, 1.15f, 1f) * size, Quaternion.identity, foliage[tint % foliage.Length]);
        }

        void Bench(Vector3 position, Quaternion turn)
        {
            for (int plank = 0; plank < 3; plank++)
            {
                Box(position + turn * new Vector3(0f, 0.52f, plank * 0.15f), new Vector3(2.1f, 0.09f, 0.12f), turn, wood);
                Box(position + turn * new Vector3(0f, 0.79f + plank * 0.14f, 0.39f), new Vector3(2.1f, 0.1f, 0.08f), turn, wood);
            }
            foreach (float x in new[] { -0.78f, 0.78f })
            {
                Rod(position + turn * new Vector3(x, 0f, 0f), position + turn * new Vector3(x, 0.54f, 0f), 0.045f, brass);
                Rod(position + turn * new Vector3(x, 0f, 0.35f), position + turn * new Vector3(x, 1.18f, 0.4f), 0.045f, brass);
            }
        }

        void Stalls()
        {
            Stall(new Vector3(9f, Ground(9f, -36f), -36f), coral, "TREATS");
            Stall(new Vector3(17f, Ground(17f, -36f), -36f), teal, "MARBLES");
            Stall(new Vector3(25f, Ground(25f, -36f), -36f), yellow, "POPCORN");
            Stall(new Vector3(-8f, Ground(-8f, -42f), -42f), teal, "TICKETS");
            for (int index = 0; index < 3; index++)
            {
                Vector3 center = new Vector3(-17f + index * 6f, Ground(-17f + index * 6f, -35f), -35f);
                Add(cylinder, center + Vector3.up * 0.62f, new Vector3(0.8f, 0.07f, 0.8f), Quaternion.identity, cream);
                Rod(center, center + Vector3.up * 2.7f, 0.045f, wood);
                StripedCanopy(center + Vector3.up * 2.45f, 1.65f, 0.65f, paints[index], cream, scenery);
                for (int chair = 0; chair < 3; chair++)
                {
                    float a = chair / 3f * Mathf.PI * 2f;
                    Vector3 seat = center + new Vector3(Mathf.Cos(a) * 1.15f, 0.39f, Mathf.Sin(a) * 1.15f);
                    Add(cylinder, seat, new Vector3(0.3f, 0.08f, 0.3f), Quaternion.identity, paints[index]);
                    Rod(seat - Vector3.up * 0.4f, seat, 0.065f, wood);
                }
            }
        }

        void Stall(Vector3 origin, Material color, string title)
        {
            Box(origin + new Vector3(0f, 0.75f, 0f), new Vector3(3.7f, 1.5f, 2.5f), Quaternion.identity, color);
            Box(origin + new Vector3(0f, 1.54f, -1.35f), new Vector3(4f, 0.16f, 0.65f), Quaternion.identity, cream);
            foreach (float x in new[] { -1.75f, 1.75f })
            foreach (float z in new[] { -1.05f, 1.05f })
                Rod(origin + new Vector3(x, 1.5f, z), origin + new Vector3(x, 3.1f, z), 0.07f, cream);
            for (int stripe = 0; stripe < 10; stripe++)
            {
                float x0 = -2.05f + stripe * 0.41f;
                float x1 = x0 + 0.41f;
                Material paint = stripe % 2 == 0 ? color : cream;
                Quad(paint, origin + new Vector3(x0, 2.9f, -1.5f), origin + new Vector3(x0, 3.7f, 0f),
                    origin + new Vector3(x1, 3.7f, 0f), origin + new Vector3(x1, 2.9f, -1.5f), new Vector3(0f, 1f, -1f));
                Quad(paint, origin + new Vector3(x0, 3.7f, 0f), origin + new Vector3(x0, 2.9f, 1.5f),
                    origin + new Vector3(x1, 2.9f, 1.5f), origin + new Vector3(x1, 3.7f, 0f), new Vector3(0f, 1f, 1f));
                Box(origin + new Vector3((x0 + x1) * 0.5f, 2.78f, -1.5f), new Vector3(0.4f, 0.25f, 0.05f), Quaternion.identity, paint);
            }
            Box(origin + new Vector3(0f, 2.2f, -1.08f), new Vector3(2.3f, 0.48f, 0.08f), Quaternion.identity, wood);
            Sign(title, origin + new Vector3(0f, 2.2f, -1.13f), Quaternion.identity, 0.026f);
            for (int item = 0; item < 7; item++)
                Add(sphere, origin + new Vector3(-1.2f + item * 0.39f, 1.77f, -1.4f), Vector3.one * 0.14f, Quaternion.identity, paints[item % paints.Length]);
        }

        void FenceAndEntrance()
        {
            for (int side = 0; side < 4; side++)
            {
                Quaternion turn = Quaternion.Euler(0f, side * 90f, 0f);
                for (int section = 0; section < 22; section++)
                {
                    float x0 = -44f + section * 4f;
                    if (side == 2 && x0 >= -4f && x0 < 4f) continue;
                    Vector3 a = turn * new Vector3(x0, 0f, 44f);
                    Vector3 b = turn * new Vector3(x0 + 4f, 0f, 44f);
                    a.y = Ground(a.x, a.z); b.y = Ground(b.x, b.z);
                    Rod(a, a + Vector3.up * 1.3f, 0.08f, cream);
                    Rod(a + Vector3.up * 0.45f, b + Vector3.up * 0.45f, 0.05f, cream);
                    Rod(a + Vector3.up * 1.05f, b + Vector3.up * 1.05f, 0.05f, cream);
                }
            }
            float y = Ground(0f, -44f);
            foreach (float x in new[] { -4.1f, 4.1f })
            {
                Box(new Vector3(x, y + 1.8f, -44f), new Vector3(0.55f, 3.6f, 0.55f), Quaternion.identity, cream);
                Add(sphere, new Vector3(x, y + 3.9f, -44f), Vector3.one * 0.32f, Quaternion.identity, coral);
            }
            Box(new Vector3(0f, y + 3.45f, -44f), new Vector3(8.6f, 0.72f, 0.22f), Quaternion.identity, teal);
            Sign("MARBLE ARENA", new Vector3(0f, y + 3.46f, -44.13f), Quaternion.identity, 0.06f);
            for (int index = 0; index < 6; index++)
            {
                Vector3 center = new Vector3(index % 2 == 0 ? -4.5f : 4.5f, 0f, -38f - index / 2 * 3.5f);
                center.y = Ground(center.x, center.z);
                Add(cylinder, center + Vector3.up * 0.35f, new Vector3(0.5f, 0.35f, 0.5f), Quaternion.identity, cream);
                Add(sphere, center + Vector3.up * 0.95f, new Vector3(0.65f, 0.6f, 0.65f), Quaternion.identity, foliage[index % foliage.Length]);
            }
        }

        void Bunting()
        {
            for (int side = 0; side < 4; side++)
            {
                Quaternion turn = Quaternion.Euler(0f, side * 90f, 0f);
                for (int segment = 0; segment < 24; segment++)
                {
                    float a = segment / 24f;
                    float b = (segment + 1) / 24f;
                    Vector3 first = turn * new Vector3(Mathf.Lerp(-20.7f, 20.7f, a), 24.8f - Mathf.Sin(a * Mathf.PI) * 1.6f, 20.7f);
                    Vector3 second = turn * new Vector3(Mathf.Lerp(-20.7f, 20.7f, b), 24.8f - Mathf.Sin(b * Mathf.PI) * 1.6f, 20.7f);
                    Rod(first, second, 0.018f, wood);
                    Vector3 middle = (first + second) * 0.5f;
                    Vector3 span = (second - first).normalized * 0.46f;
                    Material paint = paints[(segment + side) % paints.Length];
                    Triangle(paint, middle - span, middle + span, middle + Vector3.down * 0.72f, turn * Vector3.forward);
                    Triangle(paint, middle + span, middle - span, middle + Vector3.down * 0.72f, turn * Vector3.back);
                }
            }
        }

        void FestoonBulbs()
        {
            Material bulbs=Paint("Warm festoon bulbs","FFD68B",0.5f);
            bulbs.EnableKeyword("_EMISSION");
            bulbs.SetColor("_EmissionColor",new Color(1f,0.57f,0.19f)*2.2f);
            for(int line=0;line<3;line++)
            {
                Vector3 first=new Vector3(5f,1.6f+line*0.15f,-30f-line*4.5f);
                Vector3 last=new Vector3(30f,1.6f+line*0.15f,-30f-line*4.5f);
                Rod(new Vector3(first.x,Ground(first.x,first.z),first.z),first,0.065f,wood);
                Rod(new Vector3(last.x,Ground(last.x,last.z),last.z),last,0.065f,wood);
                for(int index=0;index<22;index++)
                {
                    float a=index/22f,b=(index+1)/22f;
                    Vector3 p=Vector3.Lerp(first,last,a)-Vector3.up*(Mathf.Sin(a*Mathf.PI)*0.8f);
                    Vector3 q=Vector3.Lerp(first,last,b)-Vector3.up*(Mathf.Sin(b*Mathf.PI)*0.8f);
                    Rod(p,q,0.012f,wood);
                    Add(sphere,p-Vector3.up*0.08f,new Vector3(0.075f,0.11f,0.075f),Quaternion.identity,bulbs);
                }
            }
        }

        void Carousel()
        {
            float baseY = -1.83f;
            Add(cylinder, new Vector3(0f, -1.99f, 0f), new Vector3(5.15f, 0.18f, 5.15f), Quaternion.identity, wood);
            Add(cylinder, new Vector3(0f, -1.76f, 0f), new Vector3(4.95f, 0.1f, 4.95f), Quaternion.identity, cream);
            carousel = new GameObject("Slow turning painted carousel").transform;
            carousel.SetParent(transform, false);
            carousel.localPosition = new Vector3(0f, baseY, 0f);
            Dictionary<Material, Batch> moving = new Dictionary<Material, Batch>();
            Add(cylinder, new Vector3(0f, 0.15f, 0f), new Vector3(4.75f, 0.16f, 4.75f), Quaternion.identity, teal, moving);
            Add(cylinder, new Vector3(0f, 2.8f, 0f), new Vector3(0.45f, 2.75f, 0.45f), Quaternion.identity, cream, moving);
            Add(cylinder, new Vector3(0f, 5.65f, 0f), new Vector3(4.9f, 0.22f, 4.9f), Quaternion.identity, cream, moving);
            StripedCanopy(new Vector3(0f, 5.9f, 0f), 5.25f, 1.65f, coral, cream, moving);
            Add(sphere, new Vector3(0f, 7.65f, 0f), Vector3.one * 0.3f, Quaternion.identity, brass, moving);
            for (int index = 0; index < 10; index++)
            {
                float angle = index / 10f * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 pole = radial * 3.8f;
                Rod(pole + Vector3.up * 0.3f, pole + Vector3.up * 5.5f, 0.045f, brass, moving);
                Quaternion turn = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                Vector3 horse = pole + Vector3.up * (1.45f + index % 2 * 0.45f);
                Material paint = paints[index % paints.Length];
                Add(sphere, horse, new Vector3(0.68f, 0.31f, 0.26f), turn, paint, moving);
                Add(sphere, horse + turn * new Vector3(0.53f, 0.38f, 0f), new Vector3(0.3f, 0.34f, 0.22f), turn, paint, moving);
                Add(sphere, horse + turn * new Vector3(0.72f, 0.44f, 0f), new Vector3(0.25f, 0.14f, 0.17f), turn, paint, moving);
                foreach (float x in new[] { -0.4f, 0.4f })
                foreach (float z in new[] { -0.15f, 0.15f })
                    Rod(horse + turn * new Vector3(x, -0.12f, z), horse + turn * new Vector3(x + 0.11f, -0.63f, z), 0.055f, paint, moving);
                Box(horse + Vector3.up * 0.28f, new Vector3(0.42f, 0.08f, 0.45f), turn, wood, moving);
            }
            Flush(moving, carousel, "Carousel / ");
        }

        void FerrisWheel()
        {
            Vector3 root = new Vector3(34f, Ground(34f, 9f), 9f);
            Vector3 center = root + Vector3.up * 7.5f;
            foreach (float z in new[] { -1.35f, 1.35f })
            {
                Rod(root + new Vector3(-3.9f, 0f, z), center + new Vector3(0f, 0f, z), 0.16f, cream);
                Rod(root + new Vector3(3.9f, 0f, z), center + new Vector3(0f, 0f, z), 0.16f, cream);
                Rod(root + new Vector3(-3f, 1.3f, z), root + new Vector3(3f, 1.3f, z), 0.08f, wood);
            }
            Rod(center + Vector3.back * 1.6f, center + Vector3.forward * 1.6f, 0.24f, brass);
            Box(root + new Vector3(0f, 0.18f, -2f), new Vector3(8.5f, 0.36f, 3f), Quaternion.identity, wood);
            wheel = new GameObject("Slow turning garden wheel").transform;
            wheel.SetParent(transform, false);
            wheel.localPosition = center;
            Dictionary<Material, Batch> moving = new Dictionary<Material, Batch>();
            Add(TorusShape(6.2f, 0.11f), Vector3.zero, Vector3.one, Quaternion.Euler(90f, 0f, 0f), coral, moving);
            Add(TorusShape(5.93f, 0.045f), Vector3.zero, Vector3.one, Quaternion.Euler(90f, 0f, 0f), cream, moving);
            Add(sphere, Vector3.zero, Vector3.one * 0.4f, Quaternion.identity, brass, moving);
            for (int index = 0; index < 10; index++)
            {
                float a = index / 10f * Mathf.PI * 2f;
                Vector3 point = new Vector3(Mathf.Cos(a) * 6.15f, Mathf.Sin(a) * 6.15f, 0f);
                Rod(Vector3.zero, point, 0.055f, cream, moving);
                Transform cabin = new GameObject("Painted garden wheel gondola").transform;
                cabin.SetParent(wheel, false);
                cabin.localPosition = point;
                Dictionary<Material, Batch> cabinGeometry = new Dictionary<Material, Batch>();
                Box(new Vector3(0f, -0.35f, 0f), new Vector3(1.05f, 0.65f, 0.78f), Quaternion.identity, paints[index % paints.Length], cabinGeometry);
                Box(new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 0.1f, 0.95f), Quaternion.identity, cream, cabinGeometry);
                foreach (float x in new[] { -0.45f, 0.45f })
                    Rod(new Vector3(x, -0.1f, 0f), new Vector3(x, 0.46f, 0f), 0.035f, cream, cabinGeometry);
                Flush(cabinGeometry, cabin, "Gondola / ");
                cabins.Add(cabin);
            }
            Flush(moving, wheel, "Garden wheel / ");
        }

        void StripedCanopy(Vector3 baseCenter, float radius, float height, Material first, Material second, Dictionary<Material, Batch> target)
        {
            const int panels = 24;
            for (int index = 0; index < panels; index++)
            {
                float a = index / (float)panels * Mathf.PI * 2f;
                float b = (index + 1) / (float)panels * Mathf.PI * 2f;
                Vector3 p = baseCenter + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Vector3 q = baseCenter + new Vector3(Mathf.Cos(b) * radius, 0f, Mathf.Sin(b) * radius);
                Material paint = index % 2 == 0 ? first : second;
                Get(target, paint).Triangle(baseCenter + Vector3.up * height, p, q, ((p + q) * 0.5f - baseCenter + Vector3.up * radius).normalized);
                Get(target, paint).Quad(p, q, q - Vector3.up * 0.24f, p - Vector3.up * 0.24f, ((p + q) * 0.5f - baseCenter).normalized);
            }
        }

        void Ribbon(List<Vector3> points, float width, Material material)
        {
            for (int index = 0; index < points.Count - 1; index++)
            {
                Vector3 tangent = (points[Mathf.Min(points.Count - 1, index + 1)] - points[Mathf.Max(0, index - 1)]).normalized;
                Vector3 nextTangent = (points[Mathf.Min(points.Count - 1, index + 2)] - points[index]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized * width * 0.5f;
                Vector3 nextRight = Vector3.Cross(Vector3.up, nextTangent).normalized * width * 0.5f;
                Quad(material, points[index] - right, points[index + 1] - nextRight, points[index + 1] + nextRight, points[index] + right, Vector3.up);
            }
        }

        void Disc(Vector3 center, float rx, float rz, Material material, int segments)
        {
            for (int index = 0; index < segments; index++)
            {
                float a = index / (float)segments * Mathf.PI * 2f;
                float b = (index + 1) / (float)segments * Mathf.PI * 2f;
                Triangle(material, center, center + new Vector3(Mathf.Cos(a) * rx, 0f, Mathf.Sin(a) * rz),
                    center + new Vector3(Mathf.Cos(b) * rx, 0f, Mathf.Sin(b) * rz), Vector3.up);
            }
        }

        void Sign(string text, Vector3 position, Quaternion rotation, float size)
        {
            GameObject node = new GameObject(text);
            node.transform.SetParent(transform, false);
            node.transform.localPosition = position;
            node.transform.localRotation = rotation;
            TextMesh label = node.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font; label.text = text; label.fontSize = 80; label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
            label.color = new Color(0.98f, 0.92f, 0.77f);
            node.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            node.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        void Box(Vector3 position, Vector3 size, Quaternion rotation, Material material, Dictionary<Material, Batch> target = null)
        {
            Add(cube, position, size, rotation, material, target);
        }

        void Rod(Vector3 a, Vector3 b, float radius, Material material, Dictionary<Material, Batch> target = null)
        {
            Vector3 span = b - a;
            Add(cylinder, (a + b) * 0.5f, new Vector3(radius, span.magnitude * 0.5f, radius),
                Quaternion.FromToRotation(Vector3.up, span.normalized), material, target);
        }

        void Add(Shape shape, Vector3 position, Vector3 scale, Quaternion rotation, Material material, Dictionary<Material, Batch> target = null)
        {
            Get(target ?? scenery, material).Append(shape, Matrix4x4.TRS(position, rotation, scale));
        }

        void Quad(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            Get(scenery, material).Quad(a, b, c, d, normal);
        }

        void Triangle(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            Get(scenery, material).Triangle(a, b, c, normal);
        }

        static Batch Get(Dictionary<Material, Batch> target, Material material)
        {
            if (!target.TryGetValue(material, out Batch batch)) { batch = new Batch(); target.Add(material, batch); }
            return batch;
        }

        static void Flush(Dictionary<Material, Batch> target, Transform parent, string prefix)
        {
            foreach (KeyValuePair<Material, Batch> entry in target)
            {
                GameObject node = new GameObject(prefix + entry.Key.name);
                node.transform.SetParent(parent, false);
                node.AddComponent<MeshFilter>().sharedMesh = entry.Value.ToMesh(prefix + entry.Key.name);
                MeshRenderer renderer = node.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = entry.Key;
            }
        }

        struct Shape
        {
            public Vector3[] Vertices, Normals;
            public int[] Triangles;
        }

        static Shape CubeShape()
        {
            Batch batch = new Batch();
            Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f), b = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 c = new Vector3(0.5f, -0.5f, 0.5f), d = new Vector3(-0.5f, -0.5f, 0.5f);
            batch.Quad(a, b, c, d, Vector3.down);
            batch.Quad(a + Vector3.up, d + Vector3.up, c + Vector3.up, b + Vector3.up, Vector3.up);
            batch.Quad(a, a + Vector3.up, b + Vector3.up, b, Vector3.back);
            batch.Quad(b, b + Vector3.up, c + Vector3.up, c, Vector3.right);
            batch.Quad(c, c + Vector3.up, d + Vector3.up, d, Vector3.forward);
            batch.Quad(d, d + Vector3.up, a + Vector3.up, a, Vector3.left);
            return batch.ToShape();
        }

        static Shape CylinderShape()
        {
            Batch batch = new Batch();
            for (int index = 0; index < 12; index++)
            {
                float a = index / 12f * Mathf.PI * 2f, b = (index + 1) / 12f * Mathf.PI * 2f;
                Vector3 r0 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), r1 = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                batch.Quad(r0 - Vector3.up, r0 + Vector3.up, r1 + Vector3.up, r1 - Vector3.up, (r0 + r1).normalized);
                batch.Triangle(Vector3.up, r0 + Vector3.up, r1 + Vector3.up, Vector3.up);
                batch.Triangle(Vector3.down, r1 - Vector3.up, r0 - Vector3.up, Vector3.down);
            }
            return batch.ToShape();
        }

        static Shape SphereShape()
        {
            const int rings = 10, segments = 16;
            Vector3[] vertices = new Vector3[(rings + 1) * (segments + 1)];
            List<int> triangles = new List<int>();
            for (int ring = 0; ring <= rings; ring++)
            for (int segment = 0; segment <= segments; segment++)
            {
                float phi = ring / (float)rings * Mathf.PI, theta = segment / (float)segments * Mathf.PI * 2f;
                vertices[ring * (segments + 1) + segment] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
            }
            for (int ring = 0; ring < rings; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                int a = ring * (segments + 1) + segment, b = a + segments + 1;
                Indices(triangles, vertices, a, b, a + 1, vertices[a]);
                Indices(triangles, vertices, a + 1, b, b + 1, vertices[b + 1]);
            }
            return new Shape { Vertices = vertices, Normals = vertices, Triangles = triangles.ToArray() };
        }

        static Shape TorusShape(float radius, float thickness)
        {
            const int majorSegments = 64, minorSegments = 6;
            Vector3[] vertices = new Vector3[(majorSegments + 1) * (minorSegments + 1)];
            Vector3[] normals = new Vector3[vertices.Length];
            List<int> triangles = new List<int>();
            for (int major = 0; major <= majorSegments; major++)
            for (int minor = 0; minor <= minorSegments; minor++)
            {
                float a = major / (float)majorSegments * Mathf.PI * 2f, b = minor / (float)minorSegments * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                int index = major * (minorSegments + 1) + minor;
                normals[index] = radial * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                vertices[index] = radial * radius + normals[index] * thickness;
                if (major == majorSegments || minor == minorSegments) continue;
                int next = index + minorSegments + 1;
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(next);
                triangles.Add(index + 1); triangles.Add(next + 1); triangles.Add(next);
            }
            return new Shape { Vertices = vertices, Normals = normals, Triangles = triangles.ToArray() };
        }

        static void Indices(List<int> output, Vector3[] vertices, int a, int b, int c, Vector3 normal)
        {
            bool reverse = Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), normal) < 0f;
            output.Add(a); output.Add(reverse ? c : b); output.Add(reverse ? b : c);
        }

        sealed class Batch
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            public void Append(Shape shape, Matrix4x4 matrix)
            {
                int start = vertices.Count;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                for (int index = 0; index < shape.Vertices.Length; index++)
                {
                    vertices.Add(matrix.MultiplyPoint3x4(shape.Vertices[index]));
                    normals.Add(normalMatrix.MultiplyVector(shape.Normals[index]).normalized);
                }
                foreach (int index in shape.Triangles) triangles.Add(start + index);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Triangle(a, b, c, outward); Triangle(a, c, d, outward);
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 swap = b; b = c; c = swap; }
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }

            public Shape ToShape()
            {
                return new Shape { Vertices = vertices.ToArray(), Normals = normals.ToArray(), Triangles = triangles.ToArray() };
            }

            public Mesh ToMesh(string name)
            {
                Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
