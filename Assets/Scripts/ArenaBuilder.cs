using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarbleArena
{
    public struct TrackSample
    {
        public Vector3 Position, Tangent, Right;
        public int Index;
        public float Distance;
    }

    public struct ProgressSample
    {
        public float Distance, Lateral, Width, SurfaceY;
        public int Index;
        public Vector3 Point, Tangent, Right;
        public bool InBowl, Valid;
        public string Sector;
    }

    public class ArenaBuilder : MonoBehaviour
    {
        public List<Vector3> Path = new List<Vector3>();
        public float[] Cumulative;
        public float TotalLength;
        public float TrackWidth = 2.65f;
        public float[] PulseDistances;

        const float Bank = 0.075f;
        const float WallHeight = 1.35f;
        const float WallThickness = 0.16f;
        const float FloorThickness = 0.25f;

        bool built;
        Material porcelain, shell, brass, dark, cyan, warm, white;
        Mesh cubeMesh, cylinderMesh, sphereMesh;
        readonly List<PulseGate> pulseGates = new List<PulseGate>();
        readonly List<CourseEdge> edges = new List<CourseEdge>();
        readonly List<MovingMechanism> movingMechanisms = new List<MovingMechanism>();
        readonly Vector3 bowlCenter = new Vector3(-5f, 0f, 10f);
        const float BowlRadius = 5.8f, HoleRadius = 1.2f, BowlRim = 17f, BowlHole = 12f;
        float bowlStart, bowlEnd, lowerStart;
        Vector3 bowlEntry;
        CourseEdge feeder;

        sealed class CourseEdge
        {
            public string Sector;
            public List<Vector3> Points = new List<Vector3>();
            public List<float> Widths = new List<float>();
            public float[] Arc;
            public float Length, Start, End;
        }

        sealed class MovingMechanism
        {
            public Rigidbody Body;
            public float Speed, Phase;
            public Quaternion Rest;
        }

        sealed class PulseGate
        {
            public Material Material;
            public TextMesh Label;
            public int Index;
        }

        public void Build()
        {
            if (built) return;
            built = true;
            MakeMaterials();
            MakePrimitiveMeshes();
            MakePath();
            MakeTrack();
            MakeBowl();
            MakeMechanicalObstacles();
            MakePlatform();
            MakeArchitecture();
            MakeMarkings();
            MakeEnergyGate(PulseDistances[0], 0);
            MakeEnergyGate(PulseDistances[1], 1);
            MakeRaceGate(0.8f, "MARBLE ARENA", cyan);
            MakeRaceGate(TotalLength - 2f, "FINISH", warm);
            UpdatePulseWindows(0f, 0);
            ThemePark park = new GameObject("Handcrafted miniature theme park").AddComponent<ThemePark>();
            park.transform.SetParent(transform, false);
            park.Build();
        }

        public void UpdatePulseWindows(float raceTime, int seed)
        {
            Color strong = new Color(0.96f, 0.69f, 0.24f);
            Color normal = new Color(0.2f, 0.76f, 0.81f);
            foreach (PulseGate gate in pulseGates)
            {
                float phase = Mathf.Repeat(raceTime * 0.42f + (seed % 997) * 0.013f + gate.Index * 0.37f, 1f);
                bool open = phase < 0.42f;
                bool approaching = !open && phase > 0.85f;
                float warning = approaching ? 0.5f + 0.5f * Mathf.Sin((phase - 0.85f) / 0.15f * Mathf.PI * 6f) : 0f;
                Color color = open ? strong : Color.Lerp(normal, strong, warning * 0.8f);
                gate.Material.SetColor("_BaseColor", color);
                gate.Material.SetColor("_Color", color);
                gate.Material.SetColor("_EmissionColor", color * (open ? 0.12f : warning * 0.08f));
                string state = open ? "SPRING OPEN   +3.1" : approaching ? "GET READY   +1.4" : "WINDING UP   +1.4";
                string title = (gate.Index + 1).ToString("00") + "  /  " + state;
                if (gate.Label.text != title) gate.Label.text = title;
                gate.Label.color = color;
            }
        }

        public TrackSample SampleDistance(float distance)
        {
            if (Path.Count < 2)
                return new TrackSample { Tangent = Vector3.forward, Right = Vector3.right };
            float target = Mathf.Clamp(distance, 0f, TotalLength);
            if (target > bowlStart && target < bowlEnd)
                return new TrackSample { Position = bowlEntry, Tangent = Vector3.forward, Right = Vector3.right,
                    Index = IndexAtDistance(target), Distance = target };
            int low = 0;
            int high = Cumulative.Length - 1;
            while (low + 1 < high)
            {
                int middle = (low + high) / 2;
                if (Cumulative[middle] <= target) low = middle;
                else high = middle;
            }
            int segment = Mathf.Min(low, Path.Count - 2);
            float length = Cumulative[segment + 1] - Cumulative[segment];
            float weight = Mathf.Clamp01((target - Cumulative[segment]) / Mathf.Max(length, 0.0001f));
            Vector3 tangent = Vector3.Lerp(TangentAt(segment), TangentAt(segment + 1), weight).normalized;
            return new TrackSample
            {
                Position = Vector3.Lerp(Path[segment], Path[segment + 1], weight),
                Tangent = tangent,
                Right = BankedRight(tangent),
                Index = segment,
                Distance = target
            };
        }

        public ProgressSample ClosestProgress(Vector3 position, int hint = 0)
        {
            if (Path.Count < 2) return new ProgressSample { Sector = "unbuilt" };
            float prior = Cumulative[Mathf.Clamp(hint, 0, Cumulative.Length - 1)];
            if (prior >= bowlStart - 9f && prior <= bowlEnd + 9f)
            {
                ProgressSample bowl = BowlProgress(position);
                if (bowl.Valid) return bowl;
            }
            float best = float.PositiveInfinity;
            ProgressSample result = new ProgressSample { Sector = "outside", Width = TrackWidth };
            foreach (CourseEdge edge in edges)
            {
                if (edge.End < prior - 12f || edge.Start > prior + 22f) continue;
                for (int i = 0; i < edge.Points.Count - 1; i++)
                {
                    Vector3 span = edge.Points[i + 1] - edge.Points[i];
                    float t = Mathf.Clamp01(Vector3.Dot(position - edge.Points[i], span) / Mathf.Max(span.sqrMagnitude, 0.0001f));
                    float local = Mathf.Lerp(edge.Arc[i], edge.Arc[i + 1], t);
                    float distance = Mathf.Lerp(edge.Start, edge.End, local / edge.Length);
                    if (distance < prior - 12f || distance > prior + 22f) continue;
                    Vector3 point = Vector3.Lerp(edge.Points[i], edge.Points[i + 1], t);
                    Vector3 tangent = span.normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                    float lateral = Vector3.Dot(position - point, right);
                    float width = Mathf.Lerp(edge.Widths[i], edge.Widths[i + 1], t);
                    float horizontalError = new Vector2(position.x-point.x,position.z-point.z).magnitude;
                    bool valid = Mathf.Abs(lateral) <= width * 0.5f + 0.62f
                        && horizontalError <= width * 0.5f + 0.62f
                        && position.y >= point.y - 0.5f && position.y <= point.y + 3f;
                    float score = (position - point).sqrMagnitude + (valid ? 0f : 1000f);
                    if (score >= best) continue;
                    best = score;
                    result = new ProgressSample { Distance = distance, Index = IndexAtDistance(distance), Point = point,
                        Lateral = lateral, Width = width, SurfaceY = point.y, Tangent = tangent, Right = right,
                        InBowl = false, Valid = valid, Sector = edge.Sector };
                }
            }
            Vector3[] junctions={new Vector3(8f,23.5f,-2f),new Vector3(-14f,18.3f,5f)};
            float[] junctionDistances={edges[0].End,feeder.Start};
            for(int j=0;j<junctions.Length;j++)
            {
                Vector3 offset=position-junctions[j];
                float radius=j==0?4.1f:3.1f;
                float surface=JunctionHeight(junctions[j],position);
                if(Mathf.Abs(prior-junctionDistances[j])<14f && new Vector2(offset.x,offset.z).magnitude<radius+0.6f
                    && position.y>=surface-0.4f && position.y<=surface+2.8f)
                {
                    result.Valid=true;result.SurfaceY=surface;result.Point=new Vector3(position.x,surface,position.z);
                    result.Width=radius*2f;result.Sector=j==0?"split_junction":"merge_junction";
                }
            }
            return result;
        }

        ProgressSample BowlProgress(Vector3 position)
        {
            Vector3 radial = new Vector3(position.x - bowlCenter.x, 0f, position.z - bowlCenter.z);
            float radius = radial.magnitude;
            bool inCone = radius <= BowlRadius + 0.6f && position.y >= BowlHole - 0.6f && position.y <= BowlRim + 3.6f;
            bool inThroat = radius <= HoleRadius + 0.65f && position.y >= 8.3f && position.y < BowlHole + 1f;
            bool inCup = position.x >= -8.3f && position.x <= -2.15f && Mathf.Abs(position.z - 10f) <= 3.2f
                && position.y >= CatchHeight(position.x) - 0.5f && position.y <= 12.8f;
            bool inMouth = position.x >= -14f && position.x <= -9.2f && position.z >= 5f && position.z <= 13.6f
                && position.y >= 15.4f && position.y <= 20.4f;
            float progress;
            float surface;
            Vector3 tangent;
            string sector;
            if (inCup && position.y < 10.7f)
            {
                progress = 0.9f + 0.1f * Mathf.Clamp01((position.x + 5f) / 2.7f);
                surface = CatchHeight(position.x);
                tangent = new Vector3(1f, -0.21f, 0f).normalized;
                sector = "catch_cup";
            }
            else if (position.y < BowlHole + 0.4f && radius < HoleRadius + 0.8f)
            {
                progress = 0.72f + 0.18f * Mathf.Clamp01((BowlHole + 0.4f - position.y) / 3.2f);
                surface = 9.1f;
                tangent = new Vector3(0.3f, -1f, 0f).normalized;
                sector = "funnel_drop";
            }
            else
            {
                progress = 0.72f * Mathf.Clamp01((BowlRim + 0.6f - position.y) / (BowlRim - BowlHole));
                surface = inMouth && radius > BowlRadius - 0.8f ? 17f : BowlHeight(radius);
                tangent = (Vector3.Cross(Vector3.up, radial.normalized) - radial.normalized * 0.2f + Vector3.down * 0.12f).normalized;
                sector = "funnel";
            }
            float distance = Mathf.Lerp(bowlStart, bowlEnd, progress);
            return new ProgressSample { Distance = distance, Index = IndexAtDistance(distance),
                Point = new Vector3(position.x, surface, position.z), Tangent = tangent,
                Right = Vector3.Cross(Vector3.up, tangent).normalized, Width = BowlRadius * 2f,
                SurfaceY = surface, Lateral = 0f, InBowl = true, Valid = inCone || inThroat || inCup || inMouth, Sector = sector };
        }

        int IndexAtDistance(float distance)
        {
            int low = 0, high = Cumulative.Length - 1;
            while (low + 1 < high) { int mid = (low + high) / 2; if (Cumulative[mid] <= distance) low = mid; else high = mid; }
            return low;
        }

        static float CatchHeight(float x) { return 9.6f - (x + 7.7f) * (1.1f / 5.4f); }
        static float BowlHeight(float radius) { return BowlHole + (BowlRim - BowlHole) * Mathf.Clamp01((radius - HoleRadius) / (BowlRadius - HoleRadius)); }

        public bool HasCrossedFinish(Vector3 position, Vector3 previousPos)
        {
            TrackSample end = SampleDistance(TotalLength - 2f);
            float before = Vector3.Dot(previousPos - end.Position, end.Tangent);
            float after = Vector3.Dot(position - end.Position, end.Tangent);
            float lateral = Mathf.Abs(Vector3.Dot(position - end.Position, end.Right));
            return before < 0f && after >= 0f && lateral <= TrackWidth * 0.5f + 0.1f
                && position.y >= end.Position.y - 0.1f && position.y <= end.Position.y + 1.7f;
        }

        public void TickPhysics(float raceTime, int seed, float dt)
        {
            foreach (MovingMechanism mechanism in movingMechanisms)
            {
                float clock=Mathf.Repeat(raceTime+seed%31*0.13f,4.8f);
                float gateAngle=clock<3.2f?180f:180f+360f*(clock-3.2f)/1.6f;
                float angle = mechanism.Speed < 0f ? raceTime * mechanism.Speed + mechanism.Phase : gateAngle;
                mechanism.Body.MoveRotation(Quaternion.AngleAxis(angle, Vector3.up) * mechanism.Rest);
            }
        }

        public Vector3[] StartPositions(int count)
        {
            Vector3[] result = new Vector3[Mathf.Max(0, count)];
            int rows = Mathf.CeilToInt(count / 2f);
            for (int i = 0; i < count; i++)
            {
                TrackSample sample = SampleDistance(1.6f + (rows - 1 - i / 2) * 1.24f);
                result[i] = sample.Position + sample.Right * (i % 2 == 0 ? -0.57f : 0.57f) + Vector3.up * 0.64f;
            }
            return result;
        }

        void MakePath()
        {
            Path.Clear(); edges.Clear(); TotalLength = 0f;
            List<float> canonical = new List<float>();
            CourseEdge launch = Edge("launch_drop", new[] {
                new Vector3(-18f,36f,-17f), new Vector3(-10f,32.3f,-17f), new Vector3(0f,27.6f,-17f),
                new Vector3(9f,24.3f,-16f), new Vector3(17f,24f,-12f), new Vector3(18f,23.85f,-5f),
                new Vector3(13f,23.65f,-1f), new Vector3(8f,23.5f,-2f) }, null);
            AppendPrimary(launch, canonical);
            CourseEdge inner = Edge("split_inner", new[] {
                new Vector3(8f,23.5f,-2f), new Vector3(4.5f,23f,0f), new Vector3(0f,22.3f,5f),
                new Vector3(-8f,21.5f,9f), new Vector3(-12f,21.05f,11f), new Vector3(-15f,20.65f,11f),
                new Vector3(-17.12f,20.1f,10.12f), new Vector3(-18f,19.65f,8f),
                new Vector3(-17.12f,19.2f,5.88f),new Vector3(-15f,18.65f,5f),new Vector3(-14f,18.3f,5f)
                }, new[] {2.65f,2.65f,1.4f,2.65f,2.65f,2.65f,2.65f,2.65f,2.65f,2.65f,3.1f});
            AppendPrimary(inner, canonical);
            CourseEdge outer = Edge("split_outer", new[] {
                new Vector3(8f,23.5f,-2f), new Vector3(4.5f,23f,-4f), new Vector3(3f,22.6f,-8f),
                new Vector3(-4f,21.8f,-13f), new Vector3(-13f,20.8f,-12f), new Vector3(-18f,19.6f,-6f),
                new Vector3(-17f,18.9f,1f), new Vector3(-14f,18.3f,5f) }, null);
            outer.Start = inner.Start; outer.End = inner.End; edges.Add(outer);
            feeder = Edge("merge_intake", new[] { new Vector3(-14f,18.3f,5f), new Vector3(-12.4f,17.8f,7f), new Vector3(-10.3f,17f,10f) }, new[] {3.2f,3.2f,3.1f});
            AppendPrimary(feeder, canonical);
            bowlStart = TotalLength; bowlEnd = bowlStart + 32f;
            bowlEntry = new Vector3(-9.7f, BowlHeight(4.7f), 10f);
            for (int i = 1; i <= 80; i++)
            {
                float t = i / 80f;
                float angle = Mathf.PI - Mathf.PI * 2f * t;
                float radius = Mathf.Lerp(5.3f, 0.3f, t);
                Vector3 point = bowlCenter + new Vector3(Mathf.Cos(angle) * radius, Mathf.Lerp(17f,9.05f,t), Mathf.Sin(angle) * radius);
                Path.Add(point); canonical.Add(bowlStart + t * 32f);
            }
            TotalLength = bowlEnd;
            lowerStart = TotalLength;
            CourseEdge lower = Edge("lower_rally", new[] {
                new Vector3(-2.3f,8.5f,10f), new Vector3(7f,8.2f,10f), new Vector3(17f,7.8f,4f),
                new Vector3(17f,7.4f,-8f), new Vector3(8f,7f,-12f), new Vector3(-5f,6.5f,-9f),
                new Vector3(-15f,6f,-2f), new Vector3(-16f,5.4f,12f), new Vector3(-7f,3.5f,19f),
                new Vector3(7f,1.5f,18f), new Vector3(16f,0.6f,13f) }, null);
            AppendPrimary(lower, canonical);
            Cumulative = canonical.ToArray();
            PulseDistances = new[] { launch.End - 8f, lowerStart + 7f };
        }

        CourseEdge Edge(string sector, Vector3[] controls, float[] widths)
        {
            CourseEdge edge = new CourseEdge { Sector = sector };
            for (int section = 0; section < controls.Length - 1; section++)
            {
                Vector3 p0 = controls[Mathf.Max(0,section-1)], p1 = controls[section];
                Vector3 p2 = controls[section+1], p3 = controls[Mathf.Min(controls.Length-1,section+2)];
                int steps = Mathf.Max(8, Mathf.CeilToInt(Vector3.Distance(p1,p2) / 0.32f));
                for (int i = 0; i < steps; i++)
                {
                    float t = i / (float)steps, t2=t*t, t3=t2*t;
                    Vector3 point = 0.5f * ((2f*p1)+(-p0+p2)*t+(2f*p0-5f*p1+4f*p2-p3)*t2+(-p0+3f*p1-3f*p2+p3)*t3);
                    point.y = Mathf.Lerp(p1.y,p2.y,t);
                    edge.Points.Add(point);
                    edge.Widths.Add(widths == null ? TrackWidth : Mathf.Lerp(widths[section],widths[section+1],t));
                }
            }
            edge.Points.Add(controls[controls.Length-1]); edge.Widths.Add(widths == null ? TrackWidth : widths[widths.Length-1]);
            edge.Arc = new float[edge.Points.Count];
            for(int i=1;i<edge.Points.Count;i++) { edge.Length += Vector3.Distance(edge.Points[i],edge.Points[i-1]); edge.Arc[i]=edge.Length; }
            return edge;
        }

        void AppendPrimary(CourseEdge edge, List<float> canonical)
        {
            edge.Start = TotalLength; edge.End = TotalLength + edge.Length; edges.Add(edge);
            for(int i=0;i<edge.Points.Count;i++) { Path.Add(edge.Points[i]); canonical.Add(edge.Start+edge.Arc[i]); }
            TotalLength = edge.End;
        }

        Vector3 TangentAt(int index)
        {
            return (Path[Mathf.Min(Path.Count - 1, index + 1)] - Path[Mathf.Max(0, index - 1)]).normalized;
        }

        static Vector3 BankedRight(Vector3 tangent)
        {
            return Vector3.Cross(Vector3.up, tangent).normalized;
        }

        void MakeMaterials()
        {
            porcelain = Mat("Cream glazed ceramic track", "FFF0C0", 0.02f, 0.6f);
            shell = Mat("Cobalt painted guards", "346BDE", 0.04f, 0.57f);
            brass = Mat("Polished brass joints", "D6AD47", 0.48f, 0.54f);
            dark = Mat("Midnight blue stage structure", "27344D", 0.06f, 0.39f);
            cyan = Mat("Vermilion painted pinstripe", "F34C48", 0f, 0.5f);
            warm = Mat("Sunflower painted wayfinding", "FFD14A", 0f, 0.53f);
            white = Mat("Cream painted enamel", "FFF2D0", 0f, 0.52f);
        }

        static Material Mat(string name, string html, float metallic, float smoothness, float emission = 0f)
        {
            ColorUtility.TryParseHtmlString("#" + html, out Color color);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            if (emission > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return material;
        }

        void MakeTrack()
        {
            MeshData floor = new MeshData(), walls = new MeshData(), underside = new MeshData(), markings = new MeshData(), outerMarkings=new MeshData();
            foreach(CourseEdge edge in edges)
            {
                MeshData paintedLine=edge.Sector=="split_outer"?outerMarkings:markings;
                for(int i=0;i<edge.Points.Count-1;i++)
                {
                    Vector3 p=edge.Points[i], q=edge.Points[i+1];
                    Vector3 r0=Vector3.Cross(Vector3.up, EdgeTangent(edge,i)).normalized;
                    Vector3 r1=Vector3.Cross(Vector3.up, EdgeTangent(edge,i+1)).normalized;
                    Vector3 l0=p-r0*edge.Widths[i]*0.5f, rr0=p+r0*edge.Widths[i]*0.5f;
                    Vector3 l1=q-r1*edge.Widths[i+1]*0.5f, rr1=q+r1*edge.Widths[i+1]*0.5f;
                    floor.Quad(l0,l1,rr1,rr0,Vector3.up);
                    underside.Quad(l0-Vector3.up*0.25f,rr0-Vector3.up*0.25f,rr1-Vector3.up*0.25f,l1-Vector3.up*0.25f,Vector3.down);
                    bool junction = edge.Sector.StartsWith("split") && (edge.Arc[i]<4f || edge.Length-edge.Arc[i]<3.2f)
                        || edge.Sector=="launch_drop" && edge.Length-edge.Arc[i]<2f
                        || edge.Sector=="merge_intake" && edge.Arc[i]<2f;
                    for(int side=-1;side<=1;side+=2)
                    {
                        Vector3 a=side<0?l0:rr0, b=side<0?l1:rr1;
                        if(!junction) walls.Quad(a,b,b+Vector3.up*WallHeight,a+Vector3.up*WallHeight,-r0*side);
                        underside.Quad(a,b,b-Vector3.up*0.25f,a-Vector3.up*0.25f,r0*side);
                        paintedLine.Quad(a+Vector3.up*0.012f-r0*side*0.07f,b+Vector3.up*0.012f-r1*side*0.07f,
                            b+Vector3.up*0.012f-r1*side*0.12f,a+Vector3.up*0.012f-r0*side*0.12f,Vector3.up);
                    }
                }
            }
            JunctionApron(floor,walls,new Vector3(8f,23.5f,-2f),4.1f);
            JunctionApron(floor,walls,new Vector3(-14f,18.3f,5f),3.1f);
            BranchDivider(walls);
            RenderMesh("Cream gravity course",floor.ToMesh("Continuous road surfaces"),porcelain,transform,8);
            Mesh guardVisual=walls.ToMesh("Road guards");
            RenderMesh("Painted collision guards",guardVisual,shell,transform,8);
            MakeGuardShell(guardVisual,"Road guard");
            MakeGuardPanels();
            RenderMesh("Road underside",underside.ToMesh("Road underside"),dark,transform,8);
            RenderMesh("Painted racing lines",markings.ToMesh("Painted racing lines"),cyan,transform);
            RenderMesh("Golden outer route lines",outerMarkings.ToMesh("Outer route paint"),warm,transform);
            StaticCollision("Road surfaces",floor.ToMesh("Road collision"),0.26f,0.02f);
            StaticCollision("Road guard collision",walls.ToMesh("Guard collision"),0.035f,0.13f);
        }

        static Vector3 EdgeTangent(CourseEdge edge,int i)
        {
            return (edge.Points[Mathf.Min(edge.Points.Count-1,i+1)]-edge.Points[Mathf.Max(0,i-1)]).normalized;
        }

        struct GuardBoundary
        {
            public Vector3 A,B,Normal;
            public int Count;
            public bool Side;
        }

        static Vector3Int GuardVertex(Vector3 point)
        {
            return new Vector3Int(Mathf.RoundToInt(point.x*10000f),Mathf.RoundToInt(point.y*10000f),Mathf.RoundToInt(point.z*10000f));
        }

        static (Vector3Int,Vector3Int) GuardEdge(Vector3 a,Vector3 b)
        {
            Vector3Int first=GuardVertex(a),second=GuardVertex(b);
            bool forward=first.x<second.x || first.x==second.x && (first.y<second.y || first.y==second.y && first.z<=second.z);
            return forward?(first,second):(second,first);
        }

        void MakeGuardShell(Mesh source,string title)
        {
            Vector3[] vertices=source.vertices;
            int[] indices=source.triangles;
            Dictionary<Vector3Int,Vector3> normals=new Dictionary<Vector3Int,Vector3>();
            Dictionary<(Vector3Int,Vector3Int),GuardBoundary> boundaries=new Dictionary<(Vector3Int,Vector3Int),GuardBoundary>();
            for(int triangle=0;triangle<indices.Length;triangle+=3)
            {
                Vector3 a=vertices[indices[triangle]],b=vertices[indices[triangle+1]],c=vertices[indices[triangle+2]];
                Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                bool side=Mathf.Abs(normal.y)<0.35f;
                for(int corner=0;corner<3;corner++)
                {
                    Vector3 point=vertices[indices[triangle+corner]],next=vertices[indices[triangle+(corner+1)%3]];
                    Vector3Int vertex=GuardVertex(point);
                    if(side)
                    {
                        normals.TryGetValue(vertex,out Vector3 sum);
                        normals[vertex]=sum+normal;
                    }
                    (Vector3Int,Vector3Int) key=GuardEdge(point,next);
                    if(boundaries.TryGetValue(key,out GuardBoundary boundary))
                    {
                        boundary.Count++;
                        boundaries[key]=boundary;
                    }
                    else boundaries.Add(key,new GuardBoundary{A=point,B=next,Normal=normal,Count=1,Side=side});
                }
            }
            Vector3 Outer(Vector3 point)
            {
                return normals.TryGetValue(GuardVertex(point),out Vector3 normal)?point-normal.normalized*0.12f:point;
            }
            MeshData outside=new MeshData(),caps=new MeshData();
            for(int triangle=0;triangle<indices.Length;triangle+=3)
            {
                Vector3 a=vertices[indices[triangle]],b=vertices[indices[triangle+1]],c=vertices[indices[triangle+2]];
                Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                if(Mathf.Abs(normal.y)<0.35f) outside.Triangle(Outer(a),Outer(b),Outer(c),-normal);
            }
            foreach(GuardBoundary edge in boundaries.Values)
            {
                if(edge.Count!=1 || !edge.Side) continue;
                Vector3 normal=Vector3.Cross(-edge.Normal,edge.B-edge.A).normalized;
                MeshData cap=normal.y<-.5f?outside:caps;
                cap.Quad(edge.A,edge.B,Outer(edge.B),Outer(edge.A),normal);
            }
            RenderMesh(title+" enamel exterior",outside.ToMesh(title+" outer skin"),shell,transform);
            RenderMesh(title+" cream edge caps",caps.ToMesh(title+" edge caps"),white,transform);
        }

        void MakeGuardPanels()
        {
            MeshData red=new MeshData(),yellow=new MeshData();
            foreach(CourseEdge edge in edges)
            {
                float nextPanel=5f;
                int panelIndex=0;
                for(int i=1;i<edge.Points.Count-2;i++)
                {
                    if(edge.Arc[i]<nextPanel) continue;
                    nextPanel+=6.5f;
                    bool junction=edge.Sector.StartsWith("split") && (edge.Arc[i]<4.3f || edge.Length-edge.Arc[i]<3.6f)
                        || edge.Sector=="launch_drop" && edge.Length-edge.Arc[i]<2.4f
                        || edge.Sector=="merge_intake" && edge.Arc[i]<2.4f;
                    if(junction) continue;
                    for(int side=-1;side<=1;side+=2)
                    {
                        Vector3 r0=Vector3.Cross(Vector3.up,EdgeTangent(edge,i)).normalized;
                        Vector3 r1=Vector3.Cross(Vector3.up,EdgeTangent(edge,i+2)).normalized;
                        Vector3 a=edge.Points[i]+r0*(edge.Widths[i]*.5f+.132f)*side+Vector3.up*.55f;
                        Vector3 b=edge.Points[i+2]+r1*(edge.Widths[i+2]*.5f+.132f)*side+Vector3.up*.55f;
                        MeshData paint=(panelIndex+side+1)%3==0?red:yellow;
                        paint.Quad(a,b,b+Vector3.up*.22f,a+Vector3.up*.22f,r0*side);
                    }
                    panelIndex++;
                }
            }
            RenderMesh("Guard vermilion enamel panels",red.ToMesh("Red guard panels"),cyan,transform);
            RenderMesh("Guard golden enamel panels",yellow.ToMesh("Golden guard panels"),warm,transform);
        }

        static float JunctionHeight(Vector3 center,Vector3 point)
        {
            Vector3 offset=point-center;
            return center.x>0f?center.y+offset.x*(offset.x<0f?0.14f:0.025f)
                :center.y-offset.x*0.13f-offset.z*0.16f;
        }

        void JunctionApron(MeshData floor,MeshData walls,Vector3 center,float radius)
        {
            for(int i=0;i<96;i++)
            {
                float a=i/96f*Mathf.PI*2f,b=(i+1)/96f*Mathf.PI*2f;
                Vector3 p=center+new Vector3(Mathf.Cos(a)*radius,0f,Mathf.Sin(a)*radius);
                Vector3 q=center+new Vector3(Mathf.Cos(b)*radius,0f,Mathf.Sin(b)*radius);
                p.y=JunctionHeight(center,p);q.y=JunctionHeight(center,q);
                floor.Triangle(center,p,q,Vector3.up);
                bool opening=RoadCoversJunctionEdge((p+q)*0.5f);
                if(!opening) walls.Quad(p,q,q+Vector3.up*1.45f,p+Vector3.up*1.45f,center-(p+q)*0.5f);
            }
            if(center.x>0f)
            {
                Vector3 island=center+new Vector3(-1.45f,0f,0.08f);island.y=JunctionHeight(center,island);
                for(int i=0;i<24;i++)
                {
                    float a=i/24f*Mathf.PI*2f,b=(i+1)/24f*Mathf.PI*2f;
                    Vector3 p=island+new Vector3(Mathf.Cos(a)*0.43f,0f,Mathf.Sin(a)*0.43f);
                    Vector3 q=island+new Vector3(Mathf.Cos(b)*0.43f,0f,Mathf.Sin(b)*0.43f);
                    walls.Quad(p,q,q+Vector3.up*1.25f,p+Vector3.up*1.25f,(p+q)*0.5f-island);
                    walls.Triangle(island+Vector3.up*1.25f,p+Vector3.up*1.25f,q+Vector3.up*1.25f,Vector3.up);
                }
            }
        }

        bool RoadCoversJunctionEdge(Vector3 position)
        {
            Vector2 target=new Vector2(position.x,position.z);
            foreach(CourseEdge edge in edges)
            for(int i=0;i<edge.Points.Count-1;i++)
            {
                Vector2 a=new Vector2(edge.Points[i].x,edge.Points[i].z),b=new Vector2(edge.Points[i+1].x,edge.Points[i+1].z);
                Vector2 span=b-a;
                float t=Mathf.Clamp01(Vector2.Dot(target-a,span)/Mathf.Max(span.sqrMagnitude,0.0001f));
                float y=Mathf.Lerp(edge.Points[i].y,edge.Points[i+1].y,t);
                float width=Mathf.Lerp(edge.Widths[i],edge.Widths[i+1],t);
                if(Mathf.Abs(y-position.y)<1.2f && Vector2.Distance(target,a+span*t)<width*0.5f-0.04f) return true;
            }
            return false;
        }

        void BranchDivider(MeshData walls)
        {
            CourseEdge inner=edges.Find(e=>e.Sector=="split_inner"),outer=edges.Find(e=>e.Sector=="split_outer");
            int firstA=0,firstB=0;
            while(firstA<inner.Arc.Length-2 && inner.Arc[firstA]<4f) firstA++;
            while(firstB<outer.Arc.Length-2 && outer.Arc[firstB]<4f) firstB++;
            Vector3 a=inner.Points[firstA]-Vector3.Cross(Vector3.up,EdgeTangent(inner,firstA)).normalized*inner.Widths[firstA]*0.5f;
            Vector3 b=outer.Points[firstB]+Vector3.Cross(Vector3.up,EdgeTangent(outer,firstB)).normalized*outer.Widths[firstB]*0.5f;
            Vector3 tip=new Vector3(6.12f,JunctionHeight(new Vector3(8f,23.5f,-2f),new Vector3(6.12f,0f,-1.92f)),-1.92f);
            walls.Quad(tip,a,a+Vector3.up*WallHeight,tip+Vector3.up*WallHeight,Vector3.forward);
            walls.Quad(b,tip,tip+Vector3.up*WallHeight,b+Vector3.up*WallHeight,Vector3.back);
            walls.Triangle(tip+Vector3.up*WallHeight,a+Vector3.up*WallHeight,b+Vector3.up*WallHeight,Vector3.up);
            int aIndex=firstA,bIndex=firstB;
            for(int strip=0;strip<14;strip++)
            {
                int nextA=Mathf.Min(inner.Points.Count-2,aIndex+1),nextB=Mathf.Min(outer.Points.Count-2,bIndex+1);
                Vector3 aa=inner.Points[nextA]-Vector3.Cross(Vector3.up,EdgeTangent(inner,nextA)).normalized*inner.Widths[nextA]*0.5f;
                Vector3 bb=outer.Points[nextB]+Vector3.Cross(Vector3.up,EdgeTangent(outer,nextB)).normalized*outer.Widths[nextB]*0.5f;
                walls.Quad(a+Vector3.up*WallHeight,aa+Vector3.up*WallHeight,bb+Vector3.up*WallHeight,b+Vector3.up*WallHeight,Vector3.up);
                a=aa;b=bb;aIndex=nextA;bIndex=nextB;
            }
            walls.Quad(a,b,b+Vector3.up*WallHeight,a+Vector3.up*WallHeight,Vector3.left);
        }

        void StaticCollision(string title,Mesh mesh,float friction,float bounce)
        {
            GameObject node=new GameObject(title){layer=8};node.transform.SetParent(transform,false);
            MeshCollider collider=node.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
            collider.cookingOptions=MeshColliderCookingOptions.CookForFasterSimulation|MeshColliderCookingOptions.EnableMeshCleaning|MeshColliderCookingOptions.WeldColocatedVertices|MeshColliderCookingOptions.UseFastMidphase;
            collider.sharedMaterial=new PhysicsMaterial(title){dynamicFriction=friction,staticFriction=friction,bounciness=bounce,
                frictionCombine=PhysicsMaterialCombine.Minimum,bounceCombine=PhysicsMaterialCombine.Average};
        }

        void MakeBowl()
        {
            MeshData floor=new MeshData(), walls=new MeshData(), rims=new MeshData(), cup=new MeshData();
            const int sectors=128, rings=20;
            for(int ring=0;ring<rings;ring++)
            for(int segment=0;segment<sectors;segment++)
            {
                float r0=Mathf.Lerp(HoleRadius,BowlRadius,ring/(float)rings),r1=Mathf.Lerp(HoleRadius,BowlRadius,(ring+1)/(float)rings);
                float a=segment/(float)sectors*Mathf.PI*2f,b=(segment+1)/(float)sectors*Mathf.PI*2f;
                Vector3 p=BowlPoint(r0,a),q=BowlPoint(r0,b),rr=BowlPoint(r1,b),ss=BowlPoint(r1,a);
                floor.Quad(p,q,rr,ss,Vector3.up);
                if(ring==4 || ring==11 || ring==18)
                {
                    Vector3 p2=BowlPoint(r0+0.06f,a)+Vector3.up*0.008f,q2=BowlPoint(r0+0.06f,b)+Vector3.up*0.008f;
                    rims.Quad(p+Vector3.up*0.008f,q+Vector3.up*0.008f,q2,p2,Vector3.up);
                }
                if(ring==rings-1 && Mathf.Abs(Mathf.DeltaAngle((a+b)*0.5f*Mathf.Rad2Deg,180f))>25f)
                    walls.Quad(ss,rr,rr+Vector3.up*3f,ss+Vector3.up*3f,bowlCenter-(ss+rr)*0.5f);
                if(ring==0)
                {
                    Vector3 bottomP=new Vector3(p.x,10.4f,p.z),bottomQ=new Vector3(q.x,10.4f,q.z);
                    walls.Quad(p,bottomP,bottomQ,q,bowlCenter-new Vector3(p.x,0f,p.z));
                }
            }
            Vector3 a0=new Vector3(-13.6f,17.5f,5.8f),a1=new Vector3(-13.6f,17.2f,13.1f);
            Vector3 a2=new Vector3(-9.7f,16.9f,13.1f),a3=new Vector3(-9.7f,17.2f,5.8f);
            floor.Quad(a0,a1,a2,a3,Vector3.up);
            walls.Quad(a0,a1,a1+Vector3.up*3f,a0+Vector3.up*3f,Vector3.right);
            walls.Quad(a1,a2,a2+Vector3.up*3f,a1+Vector3.up*3f,Vector3.back);
            Vector3 entryCap=new Vector3(-10.75f,17.28f,5.8f);
            walls.Quad(entryCap,a3,a3+Vector3.up*2f,entryCap+Vector3.up*2f,Vector3.forward);
            for(int i=0;i<32;i++)
            {
                float x0=Mathf.Lerp(-7.7f,-2.3f,i/32f),x1=Mathf.Lerp(-7.7f,-2.3f,(i+1)/32f);
                float w0=Mathf.Lerp(5.4f,TrackWidth,Mathf.Clamp01((x0+4f)/1.7f));
                float w1=Mathf.Lerp(5.4f,TrackWidth,Mathf.Clamp01((x1+4f)/1.7f));
                Vector3 p=new Vector3(x0,CatchHeight(x0),10f),q=new Vector3(x1,CatchHeight(x1),10f);
                cup.Quad(p-Vector3.forward*w0*0.5f,q-Vector3.forward*w1*0.5f,q+Vector3.forward*w1*0.5f,p+Vector3.forward*w0*0.5f,Vector3.up);
                foreach(int side in new[]{-1,1})
                {
                    Vector3 e0=p+Vector3.forward*(w0*0.5f*side),e1=q+Vector3.forward*(w1*0.5f*side);
                    walls.Quad(e0,e1,e1+Vector3.up*2.1f,e0+Vector3.up*2.1f,Vector3.back*side);
                }
            }
            Vector3 back0=new Vector3(-7.7f,9.6f,7.3f),back1=new Vector3(-7.7f,9.6f,12.7f);
            walls.Quad(back0,back1,back1+Vector3.up*2.1f,back0+Vector3.up*2.1f,Vector3.right);
            RenderMesh("Spiral gathering bowl",floor.ToMesh("Funnel surface"),porcelain,transform,8);
            Mesh funnelGuardVisual=walls.ToMesh("Funnel walls");
            RenderMesh("Funnel walls and throat",funnelGuardVisual,shell,transform,8);
            MakeGuardShell(funnelGuardVisual,"Funnel guard");
            RenderMesh("Funnel painted rings",rims.ToMesh("Funnel rings"),warm,transform);
            RenderMesh("Sloping catch cup",cup.ToMesh("Catch cup"),porcelain,transform,8);
            StaticCollision("Funnel surface collision",floor.ToMesh("Funnel physical surface"),0.4f,0.01f);
            StaticCollision("Funnel containment",walls.ToMesh("Funnel physical walls"),0.08f,0.03f);
            StaticCollision("Catch cup floor",cup.ToMesh("Catch cup physical floor"),0.25f,0.01f);
        }

        Vector3 BowlPoint(float radius,float angle)
        {
            return bowlCenter+new Vector3(Mathf.Cos(angle)*radius,BowlHeight(radius),Mathf.Sin(angle)*radius);
        }

        void MakeMechanicalObstacles()
        {
            GameObject agitator=new GameObject("Counter rotating bowl sweep"){layer=8};
            agitator.transform.SetParent(transform,false);agitator.transform.localPosition=bowlCenter;
            Rigidbody body=agitator.AddComponent<Rigidbody>();body.isKinematic=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            for(int arm=0;arm<2;arm++)
            {
                float sign=arm==0?1f:-1f;
                Vector3 a=new Vector3(1.6f*sign,BowlHeight(1.6f)+0.32f,0f);
                Vector3 b=new Vector3(5.2f*sign,BowlHeight(5.2f)+0.32f,0f);
                GameObject rod=Rod(a,b,0.14f,warm,agitator.transform);rod.layer=8;
                CapsuleCollider collider=rod.AddComponent<CapsuleCollider>();collider.radius=1f;collider.height=2f;
                collider.sharedMaterial=new PhysicsMaterial("Bowl sweep rubber"){dynamicFriction=0.35f,staticFriction=0.35f,bounciness=0.02f};
            }
            movingMechanisms.Add(new MovingMechanism{Body=body,Speed=-14f,Phase=12f,Rest=Quaternion.identity});
            TrackSample sample=SampleDistance(lowerStart+23f);
            GameObject gate=new GameObject("One way downstream paddle"){layer=8};gate.transform.SetParent(transform,false);
            gate.transform.localPosition=sample.Position+sample.Right*(TrackWidth*0.5f-0.1f)+Vector3.up*0.45f;
            gate.transform.localRotation=Quaternion.LookRotation(sample.Tangent,Vector3.up);
            Rigidbody gateBody=gate.AddComponent<Rigidbody>();gateBody.isKinematic=true;gateBody.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            float gateLength=TrackWidth-0.16f;
            GameObject blade=Box(new Vector3(gateLength,0.65f,0.16f),new Vector3(-gateLength*0.5f,0f,0f),warm,gate.transform);blade.layer=8;
            blade.AddComponent<BoxCollider>();
            Sphere(0.16f,new Vector3(0f,0.5f,0f),brass,gate.transform);
            movingMechanisms.Add(new MovingMechanism{Body=gateBody,Speed=1.57f,Phase=0f,Rest=gate.transform.rotation});
        }

        void MakePlatform()
        {
            Box(new Vector3(47f, 0.9f, 47f), new Vector3(0f, -2.95f, 0f), dark);
            Box(new Vector3(45.5f, 0.18f, 45.5f), new Vector3(0f, -2.42f, 0f), shell);
            Box(new Vector3(44.9f, 0.14f, 44.9f), new Vector3(0f, -2.28f, 0f), Mat("Indigo painted stage deck", "1B2344", 0f, 0.3f));
            for (int side = -1; side <= 1; side += 2)
            {
                Box(new Vector3(45.4f, 0.045f, 0.055f), new Vector3(0f, -2.29f, side * 22.69f), cyan);
                Box(new Vector3(0.055f, 0.045f, 45.4f), new Vector3(side * 22.69f, -2.29f, 0f), cyan);
            }
            foreach (float x in new[] { -20.7f, 20.7f })
            foreach (float z in new[] { -20.7f, 20.7f })
            {
                Cylinder(0.95f, 0.3f, new Vector3(x, -2.05f, z), brass);
                Cylinder(0.34f, 27f, new Vector3(x, 11.6f, z), dark);
                Cylinder(0.07f, 26.8f, new Vector3(x - 0.25f, 11.6f, z - 0.25f), white);
                foreach (float level in new[] { 0f, 7f, 14f, 21f, 25.1f })
                    Ring(0.4f, 0.04f, new Vector3(x, level, z), Quaternion.identity, brass);
                Sphere(0.25f, new Vector3(x, 25.25f, z), warm);
            }
        }

        void MakeArchitecture()
        {
            for (float distance = 9f; distance < TotalLength - 20f; distance += 13f)
            {
                if (distance > bowlStart - 3f && distance < bowlEnd + 3f) continue;
                TrackSample sample = SampleDistance(distance);
                Vector3 outside = sample.Position + sample.Right * (TrackWidth * 0.5f + 0.22f);
                Vector3 foot = new Vector3(outside.x, -2.15f, outside.z);
                Rod(foot, outside - Vector3.up * 0.1f, 0.095f, dark);
                Cylinder(0.27f, 0.16f, foot, brass);
                Rod(outside - Vector3.up * 1.5f, sample.Position - sample.Right * (TrackWidth * 0.42f) - Vector3.up * 0.28f, 0.07f, shell);
            }
        }

        void MakeMarkings()
        {
            MeshData dashes = new MeshData();
            for (float distance = 5f; distance < TotalLength - 6f; distance += 2.5f)
            {
                if (distance > bowlStart && distance < bowlEnd) continue;
                TrackSample sample = SampleDistance(distance);
                float markingWidth=Mathf.Min(TrackWidth,ClosestProgress(sample.Position,sample.Index).Width);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 point = sample.Position + sample.Right * (markingWidth * 0.39f * side) + Vector3.up * 0.018f;
                    dashes.Quad(point - sample.Right * 0.035f - sample.Tangent * 0.32f,
                        point - sample.Right * 0.035f + sample.Tangent * 0.32f,
                        point + sample.Right * 0.035f + sample.Tangent * 0.32f,
                        point + sample.Right * 0.035f - sample.Tangent * 0.32f, Vector3.up);
                }
            }
            RenderMesh("Amber lane inlays", dashes.ToMesh("Lane inlays"), warm, transform);
            MeshData darkSquares = new MeshData();
            MeshData lightSquares = new MeshData();
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 8; column++)
            {
                TrackSample sample = SampleDistance(TotalLength - 3f + row * 0.42f);
                float checkWidth=TrackWidth/8f;
                Vector3 point = sample.Position + sample.Right * ((column - 3.5f) * checkWidth) + Vector3.up * 0.024f;
                MeshData squares = (row + column) % 2 == 0 ? darkSquares : lightSquares;
                squares.Quad(point - sample.Right * (checkWidth*0.5f) - sample.Tangent * 0.21f,
                    point - sample.Right * (checkWidth*0.5f) + sample.Tangent * 0.21f,
                    point + sample.Right * (checkWidth*0.5f) + sample.Tangent * 0.21f,
                    point + sample.Right * (checkWidth*0.5f) - sample.Tangent * 0.21f, Vector3.up);
            }
            RenderMesh("Finish dark checks", darkSquares.ToMesh("Dark checks"), dark, transform);
            RenderMesh("Finish pearl checks", lightSquares.ToMesh("Pearl checks"), white, transform);
        }

        void MakeEnergyGate(float distance, int index)
        {
            TrackSample sample = SampleDistance(distance);
            Material gateMaterial = new Material(warm) { name = "Pulse station " + (index + 1) };
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, sample.Tangent);
            Ring(2.85f, 0.12f, sample.Position + Vector3.up * 2f, rotation, dark);
            Ring(2.85f, 0.055f, sample.Position - sample.Tangent * 0.16f + Vector3.up * 2f, rotation, gateMaterial);
            Vector3 dialCenter = sample.Position - sample.Tangent * 0.17f + Vector3.up * 2f;
            for (int tick = 0; tick < 20; tick++)
            {
                float angle = tick / 20f * Mathf.PI * 2f;
                Vector3 radial = rotation * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float inner = tick % 5 == 0 ? 2.62f : 2.72f;
                Rod(dialCenter + radial * inner, dialCenter + radial * 2.82f, 0.025f, brass);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 edge = sample.Position + sample.Right * ((TrackWidth * 0.5f + 0.32f) * side);
                Box(new Vector3(0.45f, 0.4f, 0.6f), edge + Vector3.up * 0.1f, white);
                Cylinder(0.065f, 1.12f, edge + Vector3.up * 0.76f, dark);
                Spring(edge + Vector3.up * 0.33f, 0.18f, 0.82f, brass);
                Sphere(0.14f, edge + Vector3.up * 1.4f, gateMaterial);
            }
            TextMesh label = MountedSign((index + 1).ToString("00") + "  /  PULSE",
                sample.Position - sample.Tangent * 0.17f + Vector3.up * 4.63f, sample.Tangent,
                4.75f, 0.43f, 0.026f, gateMaterial, new Color(1f, 0.85f, 0.55f));
            pulseGates.Add(new PulseGate { Material = gateMaterial, Label = label, Index = index });
        }

        void MakeRaceGate(float distance, string title, Material accent)
        {
            TrackSample sample = SampleDistance(distance);
            Vector3 left = sample.Position - sample.Right * 2.5f + Vector3.up * 3.7f;
            Vector3 right = sample.Position + sample.Right * 2.5f + Vector3.up * 3.7f;
            Rod(sample.Position - sample.Right * 2.5f - Vector3.up * 0.25f, left, 0.1f, brass);
            Rod(sample.Position + sample.Right * 2.5f - Vector3.up * 0.25f, right, 0.1f, brass);
            Rod(left, right, 0.12f, dark);
            Rod(left + Vector3.up * 0.19f, right + Vector3.up * 0.19f, 0.035f, accent);
            MountedSign(title, sample.Position + Vector3.up * 3.72f, sample.Tangent,
                4.7f, 0.5f, title == "FINISH" ? 0.05f : 0.035f, accent, new Color(0.91f, 0.98f, 0.97f));
        }

        TextMesh MountedSign(string text, Vector3 position, Vector3 tangent, float width, float height,
            float characterSize, Material accent, Color color)
        {
            Transform holder = new GameObject("Mounted station plaque").transform;
            holder.SetParent(transform, false);
            holder.localPosition = position;
            holder.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
            Box(new Vector3(width, height, 0.13f), Vector3.zero, dark, holder);
            Box(new Vector3(width - 0.12f, 0.025f, 0.025f), new Vector3(0f, -height * 0.5f + 0.045f, -0.075f), accent, holder);
            TextMesh label = AddLabel(text, Vector3.zero, color, characterSize);
            label.transform.SetParent(holder, false);
            label.transform.localPosition = new Vector3(0f, 0.025f, -0.085f);
            label.transform.localRotation = Quaternion.identity;
            return label;
        }

        TextMesh AddLabel(string text, Vector3 position, Color color, float size)
        {
            GameObject node = new GameObject(text);
            node.transform.SetParent(transform, false);
            node.transform.localPosition = position;
            TextMesh label = node.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font;
            label.text = text;
            label.fontSize = 80;
            label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = color;
            node.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            node.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return label;
        }

        GameObject Box(Vector3 size, Vector3 position, Material material, Transform parent = null)
        {
            GameObject node = RenderMesh("Enamel panel", cubeMesh, material, parent != null ? parent : transform);
            node.transform.localPosition = position;
            node.transform.localScale = size;
            return node;
        }

        GameObject Cylinder(float radius, float height, Vector3 position, Material material, Transform parent = null)
        {
            GameObject node = RenderMesh("Turned metal support", cylinderMesh, material, parent != null ? parent : transform);
            node.transform.localPosition = position;
            node.transform.localScale = new Vector3(radius, height * 0.5f, radius);
            return node;
        }

        GameObject Rod(Vector3 first, Vector3 second, float radius, Material material, Transform parent = null)
        {
            Vector3 span = second - first;
            GameObject node = Cylinder(radius, span.magnitude, (first + second) * 0.5f, material, parent);
            if (span.sqrMagnitude > 0.0001f) node.transform.localRotation = Quaternion.FromToRotation(Vector3.up, span.normalized);
            return node;
        }

        GameObject Sphere(float radius, Vector3 position, Material material, Transform parent = null)
        {
            GameObject node = RenderMesh("Luminous bearing", sphereMesh, material, parent != null ? parent : transform);
            node.transform.localPosition = position;
            node.transform.localScale = Vector3.one * radius;
            return node;
        }

        GameObject Ring(float radius, float thickness, Vector3 position, Quaternion rotation, Material material, Transform parent = null)
        {
            const int majorSegments = 64;
            const int minorSegments = 8;
            Vector3[] vertices = new Vector3[(majorSegments + 1) * (minorSegments + 1)];
            Vector3[] normals = new Vector3[vertices.Length];
            List<int> triangles = new List<int>(majorSegments * minorSegments * 6);
            for (int major = 0; major <= majorSegments; major++)
            for (int minor = 0; minor <= minorSegments; minor++)
            {
                float a = major / (float)majorSegments * Mathf.PI * 2f;
                float b = minor / (float)minorSegments * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 normal = radial * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                int index = major * (minorSegments + 1) + minor;
                vertices[index] = radial * radius + normal * thickness;
                normals[index] = normal;
                if (major == majorSegments || minor == minorSegments) continue;
                int next = index + minorSegments + 1;
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(next);
                triangles.Add(index + 1); triangles.Add(next + 1); triangles.Add(next);
            }
            Mesh mesh = new Mesh { name = "Machined light ring" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            GameObject node = RenderMesh("Machined light ring", mesh, material, parent != null ? parent : transform);
            node.transform.localPosition = position;
            node.transform.localRotation = rotation;
            return node;
        }

        void Spring(Vector3 position, float radius, float height, Material material)
        {
            const int steps = 112, sides = 6;
            Vector3[] vertices = new Vector3[(steps + 1) * (sides + 1)];
            Vector3[] normals = new Vector3[vertices.Length];
            List<int> triangles = new List<int>(steps * sides * 6);
            for (int step = 0; step <= steps; step++)
            {
                float fraction = step / (float)steps;
                float angle = fraction * Mathf.PI * 14f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangent = new Vector3(-Mathf.Sin(angle) * radius, height / (Mathf.PI * 14f), Mathf.Cos(angle) * radius).normalized;
                Vector3 secondAxis = Vector3.Cross(tangent, radial).normalized;
                Vector3 center = radial * radius + Vector3.up * (height * fraction);
                for (int side = 0; side <= sides; side++)
                {
                    float crossAngle = side / (float)sides * Mathf.PI * 2f;
                    int index = step * (sides + 1) + side;
                    normals[index] = radial * Mathf.Cos(crossAngle) + secondAxis * Mathf.Sin(crossAngle);
                    vertices[index] = center + normals[index] * 0.025f;
                    if (step == steps || side == sides) continue;
                    int next = index + sides + 1;
                    triangles.Add(index); triangles.Add(index + 1); triangles.Add(next);
                    triangles.Add(index + 1); triangles.Add(next + 1); triangles.Add(next);
                }
            }
            Mesh mesh = new Mesh { name = "Mechanical spring" };
            mesh.vertices = vertices; mesh.normals = normals; mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            GameObject node = RenderMesh("Visible mechanical spring", mesh, material, transform);
            node.transform.localPosition = position;
        }

        static GameObject RenderMesh(string name, Mesh mesh, Material material, Transform parent, int layer = 0)
        {
            GameObject node = new GameObject(name) { layer = layer };
            node.transform.SetParent(parent, false);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = true;
            return node;
        }

        void MakePrimitiveMeshes()
        {
            MeshData cube = new MeshData();
            Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f);
            Vector3 b = new Vector3(0.5f, -0.5f, -0.5f);
            Vector3 c = new Vector3(0.5f, -0.5f, 0.5f);
            Vector3 d = new Vector3(-0.5f, -0.5f, 0.5f);
            cube.Quad(a, b, c, d, Vector3.down);
            cube.Quad(a + Vector3.up, d + Vector3.up, c + Vector3.up, b + Vector3.up, Vector3.up);
            cube.Quad(a, a + Vector3.up, b + Vector3.up, b, Vector3.back);
            cube.Quad(b, b + Vector3.up, c + Vector3.up, c, Vector3.right);
            cube.Quad(c, c + Vector3.up, d + Vector3.up, d, Vector3.forward);
            cube.Quad(d, d + Vector3.up, a + Vector3.up, a, Vector3.left);
            cubeMesh = cube.ToMesh("Unit enamel panel");

            MeshData cylinder = new MeshData();
            const int cylinderSegments = 20;
            for (int index = 0; index < cylinderSegments; index++)
            {
                float angle0 = index / (float)cylinderSegments * Mathf.PI * 2f;
                float angle1 = (index + 1) / (float)cylinderSegments * Mathf.PI * 2f;
                Vector3 r0 = new Vector3(Mathf.Cos(angle0), 0f, Mathf.Sin(angle0));
                Vector3 r1 = new Vector3(Mathf.Cos(angle1), 0f, Mathf.Sin(angle1));
                cylinder.Quad(r0 - Vector3.up, r0 + Vector3.up, r1 + Vector3.up, r1 - Vector3.up, (r0 + r1).normalized);
                cylinder.Triangle(Vector3.up, r0 + Vector3.up, r1 + Vector3.up, Vector3.up);
                cylinder.Triangle(Vector3.down, r1 - Vector3.up, r0 - Vector3.up, Vector3.down);
            }
            cylinderMesh = cylinder.ToMesh("Unit turned support");

            const int rings = 16;
            const int segments = 24;
            Vector3[] sphereVertices = new Vector3[(rings + 1) * (segments + 1)];
            List<int> sphereTriangles = new List<int>();
            for (int ring = 0; ring <= rings; ring++)
            for (int segment = 0; segment <= segments; segment++)
            {
                float phi = ring / (float)rings * Mathf.PI;
                float theta = segment / (float)segments * Mathf.PI * 2f;
                sphereVertices[ring * (segments + 1) + segment] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
            }
            for (int ring = 0; ring < rings; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                int index = ring * (segments + 1) + segment;
                int next = index + segments + 1;
                AddOrientedTriangle(sphereTriangles, sphereVertices, index, next, index + 1, sphereVertices[index], true);
                AddOrientedTriangle(sphereTriangles, sphereVertices, index + 1, next, next + 1, sphereVertices[next + 1], true);
            }
            sphereMesh = new Mesh { name = "Unit luminous bearing" };
            sphereMesh.vertices = sphereVertices;
            sphereMesh.normals = sphereVertices;
            sphereMesh.SetTriangles(sphereTriangles, 0);
            sphereMesh.RecalculateBounds();
        }

        static void AddOrientedTriangle(List<int> output, Vector3[] vertices, int a, int b, int c, Vector3 normal, bool orient)
        {
            output.Add(a);
            bool reverse = orient && Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), normal) < 0f;
            output.Add(reverse ? c : b);
            output.Add(reverse ? b : c);
        }

        sealed class MeshData
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                Vector3 half = size * 0.5f;
                Vector3 a = center + rotation * new Vector3(-half.x, -half.y, -half.z);
                Vector3 b = center + rotation * new Vector3(half.x, -half.y, -half.z);
                Vector3 c = center + rotation * new Vector3(half.x, -half.y, half.z);
                Vector3 d = center + rotation * new Vector3(-half.x, -half.y, half.z);
                Vector3 up = rotation * new Vector3(0f, size.y, 0f);
                Quad(a, b, c, d, rotation * Vector3.down);
                Quad(a + up, d + up, c + up, b + up, rotation * Vector3.up);
                Quad(a, a + up, b + up, b, rotation * Vector3.back);
                Quad(b, b + up, c + up, c, rotation * Vector3.right);
                Quad(c, c + up, d + up, d, rotation * Vector3.forward);
                Quad(d, d + up, a + up, a, rotation * Vector3.left);
            }

            public void ChamferedTile(Vector3 center, float size, float cut, float depth)
            {
                float h = size * 0.5f;
                Vector3[] edge =
                {
                    new Vector3(-h + cut, 0f, -h), new Vector3(h - cut, 0f, -h),
                    new Vector3(h, 0f, -h + cut), new Vector3(h, 0f, h - cut),
                    new Vector3(h - cut, 0f, h), new Vector3(-h + cut, 0f, h),
                    new Vector3(-h, 0f, h - cut), new Vector3(-h, 0f, -h + cut)
                };
                for (int index = 0; index < edge.Length; index++)
                {
                    Vector3 a = center + edge[index];
                    Vector3 b = center + edge[(index + 1) % edge.Length];
                    Triangle(center, a, b, Vector3.up);
                    Vector3 outward = ((a + b) * 0.5f - center).normalized;
                    Quad(a, b, b - Vector3.up * depth, a - Vector3.up * depth, outward);
                }
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                {
                    Vector3 oldB = b;
                    b = d;
                    d = oldB;
                }
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int index = 0; index < 4; index++) normals.Add(normal);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                {
                    Vector3 swap = b; b = c; c = swap;
                }
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }

            public Mesh ToMesh(string name)
            {
                Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
