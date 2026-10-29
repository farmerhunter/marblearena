using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarbleArena
{
    public sealed class MarbleRacer : MonoBehaviour
    {
        public const float Radius = .52f;
        public RacerDefinition Definition;
        public Rigidbody Body;
        public Vector3 TrackTangent = Vector3.forward;
        public Vector3 TrackRight=Vector3.right;
        public Vector3 LastPhysicsPosition,LastSafePosition;
        public bool InBowl;
        public string Sector;
        public float Progress, FinishTime = -1, Penalty, StuckTime, BestProgress;
        public int PathIndex, Rank, Rescues;
        public bool BoostUsed, NudgeUsed, Supported;
        public bool[] Coils = new bool[2];
        public float AiBoostAt, AiNudgeAt, AiNudgeDirection;
        public Action<MarbleRacer, float> Impact;
        public Action<MarbleRacer, float> PhysicalImpact;
        public bool Finished => FinishTime >= 0;
        Transform visual;
        GameObject supportMarker;
        TrailRenderer trail;
        float collisionCooldown, pulse;

        public void Setup(RacerDefinition definition, bool supported)
        {
            Definition = definition;
            Supported = supported;
            name = definition.name_en;
            gameObject.layer = 9;
            Body = gameObject.AddComponent<Rigidbody>();
            Body.mass = 1;
            Body.linearDamping = definition.family == "speed" ? .004f : .008f;
            Body.angularDamping = .012f;
            Body.sleepThreshold = 0f;
            Body.maxAngularVelocity = 80;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.solverIterations = 12;
            Body.solverVelocityIterations = 6;
            Body.isKinematic = true;
            var collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = Radius;
            var physics = new PhysicsMaterial(definition.family)
            {
                dynamicFriction = definition.family == "grip" ? .48f : .32f,
                staticFriction = .48f,
                bounciness = definition.family == "bounce" ? .27f : .09f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Average
            };
            collider.sharedMaterial = physics;
            visual = new GameObject("Handmade marble").transform;
            visual.SetParent(transform, false);
            var tint = definition.Color;
            if(MarbleArt.IsGlass(definition))
            {
                var glass = new Material(Shader.Find("MarbleArena/PrismGlass"));
                tint.a = definition.marble_pattern=="honey"?.42f:.3f;
                glass.SetColor("_Tint", tint);
                glass.SetColor("_Accent", definition.Accent);
                var shell = Visuals.Primitive("Colored glass", PrimitiveType.Sphere, visual, Vector3.zero, Vector3.one * Radius * 2, glass);
                shell.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                BuildCatEye();
            }
            else
            {
                var surface=Visuals.Material(definition.name_en+" glazed pattern",Color.white,0,definition.marble_pattern=="speckle"?.5f:.77f);
                surface.SetTexture("_BaseMap",MarbleArt.SurfaceTexture(definition));
                Visuals.Primitive("Patterned marble",PrimitiveType.Sphere,visual,Vector3.zero,Vector3.one*Radius*2,surface);
            }
            var trailMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            trailMaterial.SetColor("_BaseColor", definition.Color);
            trailMaterial.SetFloat("_Surface", 1);
            trailMaterial.SetFloat("_Blend", 0);
            trailMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            trailMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            trailMaterial.SetFloat("_ZWrite", 0);
            trailMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            trailMaterial.renderQueue = 3000;
            trail = gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMaterial;
            trail.time = .12f;
            trail.minVertexDistance = .15f;
            trail.widthMultiplier = .035f;
            trail.startColor = new Color(tint.r, tint.g, tint.b, .28f);
            trail.endColor = new Color(tint.r, tint.g, tint.b, 0);
            trail.emitting = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            if (supported)
            {
                var mark=new Mesh {name="Support pennant",vertices=new[]{new Vector3(-.12f,.15f,0),new Vector3(.12f,.15f,0),new Vector3(0,-.05f,0)},triangles=new[]{0,1,2}};
                mark.RecalculateNormals();
                supportMarker = Visuals.MeshObject("Your supported marble",mark,null,
                    Visuals.Material("Small paper pennant",new Color(.95f,.77f,.36f),0,.25f));
            }
        }

        void BuildCatEye()
        {
            for(int leaf=0;leaf<3;leaf++)
            {
                const int steps=40;
                var vertices=new Vector3[(steps+1)*2];
                var triangles=new int[steps*6];
                for(int i=0;i<=steps;i++)
                {
                    float h=i/(float)steps*2-1;
                    float a=h*2.7f+leaf*Mathf.PI*2/3;
                    Vector3 right=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    float width=Radius*.63f*Mathf.Pow(Mathf.Max(0,1-h*h),.8f);
                    vertices[i*2]=Vector3.up*h*Radius*.79f-right*width*.18f;
                    vertices[i*2+1]=Vector3.up*h*Radius*.79f+right*width;
                    if(i==steps) continue;
                    int k=i*2,t=i*6;
                    int[] face={k,k+2,k+1,k+1,k+2,k+3};
                    for(int j=0;j<6;j++) triangles[t+j]=face[j];
                }
                var mesh=new Mesh {name="Poured cat-eye petal",vertices=vertices,triangles=triangles};
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                Color color=leaf==0?Definition.Accent:leaf==1?Color.Lerp(Definition.Color,Color.white,.6f):Definition.Color*.65f;
                var pigment=Visuals.Material("Cat-eye pigment",color,0,.65f);
                pigment.SetFloat("_Cull",0);
                Visuals.MeshObject("Poured glass petal",mesh,visual,pigment);
            }
        }

        public void TickVisual(float delta, bool racing)
        {
            collisionCooldown = Mathf.Max(0,collisionCooldown-delta);
            pulse = Mathf.MoveTowards(pulse,0,delta*2);
            visual.localScale = Vector3.one * (1 + pulse*.07f);
            trail.emitting = racing && !Finished && Body.linearVelocity.sqrMagnitude > 100;
            if (supportMarker)
            {
                supportMarker.transform.position = transform.position + Vector3.up*(Radius+.18f);
                if(Camera.main) supportMarker.transform.rotation=Camera.main.transform.rotation;
            }
        }

        public void Impulse(Vector3 impulse)
        {
            Body.AddForce(impulse,ForceMode.Impulse);
            pulse = 1;
        }

        public void ClearTrail() { if (trail) trail.Clear(); }

        void OnCollisionEnter(Collision collision)
        {
            float strength = collision.relativeVelocity.magnitude;
            PhysicalImpact?.Invoke(this,strength);
            if (strength > .8f && collisionCooldown <= 0)
            {
                collisionCooldown = .11f;
                Impact?.Invoke(this,strength);
            }
        }

        void OnDestroy()
        {
            if (supportMarker)
            {
                if(Application.isPlaying) Destroy(supportMarker);
                else DestroyImmediate(supportMarker);
            }
        }
    }
}
