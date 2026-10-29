using System;
using System.IO;
using System.Linq;
using MarbleArena;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BuildGame
{
    [MenuItem("Marble Arena/Prepare project")]
    public static void Prepare()
    {
        Directory.CreateDirectory("Assets/Settings");
        Directory.CreateDirectory("Assets/Scenes");
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PrismURP.asset");
        if(!pipeline)
        {
            pipeline=UniversalRenderPipelineAsset.Create();
            AssetDatabase.CreateAsset(pipeline,"Assets/Settings/PrismURP.asset");
            var serialized=new SerializedObject(pipeline);
            var renderers=serialized.FindProperty("m_RendererDataList");
            if(renderers!=null)
            {
                for(int i=0;i<renderers.arraySize;i++)
                {
                    var renderer=renderers.GetArrayElementAtIndex(i).objectReferenceValue;
                    if(renderer && !AssetDatabase.Contains(renderer))
                    {
                        AssetDatabase.CreateAsset(renderer,"Assets/Settings/PrismRenderer"+i+".asset");
                        ResourceReloader.ReloadAllNullIn(renderer,"Packages/com.unity.render-pipelines.universal");
                    }
                }
            }
        }
        pipeline.supportsHDR=true;
        pipeline.msaaSampleCount=4;
        pipeline.renderScale=.75f;
        pipeline.shadowDistance=55;
        pipeline.shadowCascadeCount=2;
        var pipelineSettings=new SerializedObject(pipeline);
        pipelineSettings.FindProperty("m_SoftShadowsSupported").boolValue=true;
        pipelineSettings.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true;
        pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
        pipeline.additionalLightsShadowmapResolution=2048;
        pipeline.supportsCameraDepthTexture=true;
        pipeline.supportsCameraOpaqueTexture=true;
        GraphicsSettings.defaultRenderPipeline=pipeline;
        QualitySettings.renderPipeline=pipeline;
        QualitySettings.vSyncCount=0;
        Physics.simulationMode=SimulationMode.FixedUpdate;
        EditorUtility.SetDirty(pipeline);
        PlayerSettings.companyName="Marble Park";
        PlayerSettings.productName="Marble Arena";
        PlayerSettings.bundleVersion="0.1.0";
        PlayerSettings.SplashScreen.show=false;
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"cn.marblearena.game");
        PlayerSettings.colorSpace=ColorSpace.Linear;
        PlayerSettings.defaultScreenWidth=2560;
        PlayerSettings.defaultScreenHeight=1600;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.resizableWindow=true;
        PlayerSettings.runInBackground=true;
        PlayerSettings.macRetinaSupport=true;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.SetArchitecture(UnityEditor.Build.NamedBuildTarget.Standalone,1);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX,new[]{GraphicsDeviceType.Metal});
        var projectSettings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var input=projectSettings.FindProperty("activeInputHandler");
        if(input!=null) input.intValue=0;
        projectSettings.ApplyModifiedPropertiesWithoutUndo();
        var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        tags.FindProperty("layers").GetArrayElementAtIndex(8).stringValue="Race Track";
        tags.FindProperty("layers").GetArrayElementAtIndex(9).stringValue="Marbles";
        tags.ApplyModifiedPropertiesWithoutUndo();
        IncludeShaders();
        BakeMarbleArt();
        ConfigureAmbientOcclusion();
        foreach(string guid in AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/Resources/Audio"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);
            AudioImporterSampleSettings settings=importer.defaultSampleSettings;
            settings.loadType=path.Contains("music")?AudioClipLoadType.Streaming:AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=AudioCompressionFormat.Vorbis;
            settings.quality=.8f;
            importer.defaultSampleSettings=settings;
            importer.SaveAndReimport();
        }
        if(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SunloopPark.unity") && !AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MidnightMidway.unity"))
            AssetDatabase.MoveAsset("Assets/Scenes/SunloopPark.unity","Assets/Scenes/MidnightMidway.unity");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Marble Arena").AddComponent<RaceDirector>();
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/MidnightMidway.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/MidnightMidway.unity",true)};
        AssetDatabase.SaveAssets();
        Debug.Log("MARBLE_PROJECT_READY");
    }

    static void IncludeShaders()
    {
        var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var shaders=settings.FindProperty("m_AlwaysIncludedShaders");
        string[] alwaysIncluded={"MarbleArena/PrismGlass","Skybox/Procedural","UI/Default","Hidden/CubeCopy","Hidden/CubeBlur","Hidden/CubeBlend"};
        shaders.arraySize=alwaysIncluded.Length;
        for(int i=0;i<alwaysIncluded.Length;i++)
        {
            Shader shader=Shader.Find(alwaysIncluded[i]);
            if(!shader) throw new InvalidOperationException("Shader missing: "+alwaysIncluded[i]);
            shaders.GetArrayElementAtIndex(i).objectReferenceValue=shader;
        }
        settings.FindProperty("m_FogStripping").intValue=1;
        settings.FindProperty("m_FogKeepLinear").boolValue=false;
        settings.FindProperty("m_FogKeepExp").boolValue=true;
        settings.FindProperty("m_FogKeepExp2").boolValue=false;
        settings.ApplyModifiedPropertiesWithoutUndo();

        if(!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets","Resources");
        if(!AssetDatabase.IsValidFolder("Assets/Resources/Rendering")) AssetDatabase.CreateFolder("Assets/Resources","Rendering");
        SaveTemplate("LitBase","Universal Render Pipeline/Lit",false,false);
        SaveTemplate("LitEmission","Universal Render Pipeline/Lit",true,false);
        SaveTemplate("ParticleTrail","Universal Render Pipeline/Particles/Unlit",false,true);
        AssetDatabase.SaveAssets();

        void SaveTemplate(string name,string shaderName,bool emission,bool transparent)
        {
            Shader shader=Shader.Find(shaderName);
            if(!shader) throw new InvalidOperationException("Shader missing: "+shaderName);
            string path="Assets/Resources/Rendering/"+name+".mat";
            Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)
            {
                material=new Material(shader){name=name};
                AssetDatabase.CreateAsset(material,path);
            }
            material.shader=shader;
            // Resources assets preserve the exact runtime shader_feature combinations without retaining every URP variant.
            material.shaderKeywords=Array.Empty<string>();
            material.enableInstancing=!transparent;
            material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_Surface",transparent?1:0);
            material.SetFloat("_Blend",0);
            material.SetFloat("_SrcBlend",(float)(transparent?BlendMode.SrcAlpha:BlendMode.One));
            material.SetFloat("_DstBlend",(float)(transparent?BlendMode.OneMinusSrcAlpha:BlendMode.Zero));
            material.SetFloat("_ZWrite",transparent?0:1);
            material.SetColor("_EmissionColor",emission?Color.white*2:Color.black);
            material.globalIlluminationFlags=emission?MaterialGlobalIlluminationFlags.RealtimeEmissive:MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            material.SetOverrideTag("RenderType",transparent?"Transparent":"Opaque");
            material.renderQueue=transparent?(int)RenderQueue.Transparent:(int)RenderQueue.Geometry;
            if(emission) material.EnableKeyword("_EMISSION");
            if(transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(material);
        }
    }

    [MenuItem("Marble Arena/Build Mac application")]
    public static void Mac()
    {
        Prepare();
        Directory.CreateDirectory("build");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes=new[]{"Assets/Scenes/MidnightMidway.unity"},
            locationPathName="build/Marble Arena.app",
            target=BuildTarget.StandaloneOSX,
            options=BuildOptions.None
        });
        Debug.Log("MARBLE_BUILD "+report.summary.result+" bytes="+report.summary.totalSize);
        if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Mac build failed");
    }

    public static void PhysicsTrial()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Physics trial").AddComponent<RaceDirector>();
        root.Automated=true;
        root.Boot(false);
        Physics.simulationMode=SimulationMode.Script;
        root.ReleaseRace();
        Physics.SyncTransforms();
        for(int frame=0;frame<120*230 && root.State=="racing";frame++)
        {
            root.StepRace(1f/120);
            Physics.Simulate(1f/120);
            if(frame%1200==0) Debug.Log("TRIAL t="+root.RaceTime.ToString("F1")+" progress="+string.Join("; ",root.Racers.Select(r=>$"{r.Definition.id}:{r.Progress:F1} {r.Sector} p={r.Body.position:F2} v={r.Body.linearVelocity.magnitude:F2}")));
            if(Environment.GetCommandLineArgs().Contains("--fail-fast") && root.Racers.Any(r=>r.Rescues>0)) break;
        }
        string path=Path.GetFullPath("test-output/physics-trial.json");
        string[] args=Environment.GetCommandLineArgs();
        int reportArg=Array.IndexOf(args,"--report");
        if(reportArg>=0 && reportArg+1<args.Length) path=args[reportArg+1];
        root.WriteReport(path);
        Physics.simulationMode=SimulationMode.FixedUpdate;
        int finishers=root.Racers.Count(r=>r.Finished);
        Debug.Log("PHYSICS_TRIAL_COMPLETE finishers="+finishers+"/"+root.Racers.Count+" recoveries="+root.Racers.Sum(r=>r.Rescues));
        if(finishers!=root.Racers.Count) throw new Exception("Some marbles did not finish");
        if(root.Racers.Any(r=>r.Rescues>0)) throw new Exception("Track required recovery; this is not a clean race");
    }

    static void BakeMarbleArt()
    {
        Directory.CreateDirectory("Assets/Resources/MarbleTextures");
        Directory.CreateDirectory("Assets/Resources/MarblePortraits");
        var roster=JsonUtility.FromJson<RosterFile>("{\"items\":"+File.ReadAllText("Assets/Resources/Data/roster.json")+"}").items;
        foreach(var racer in roster)
        {
            string surface="Assets/Resources/MarbleTextures/"+racer.id+".png";
            string portrait="Assets/Resources/MarblePortraits/"+racer.id+".png";
            DateTime artChanged=new[]{File.GetLastWriteTimeUtc("Assets/Resources/Data/roster.json"),File.GetLastWriteTimeUtc("Assets/Scripts/MarbleArt.cs")}.Max();
            if(File.Exists(surface) && File.Exists(portrait) && File.GetLastWriteTimeUtc(surface)>artChanged && File.GetLastWriteTimeUtc(portrait)>artChanged) continue;
            Texture2D texture=MarbleArt.GenerateSurface(racer);
            File.WriteAllBytes(surface,texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            texture=MarbleArt.GeneratePortrait(racer);
            File.WriteAllBytes(portrait,texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(surface,ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(portrait,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(surface);
            importer.sRGBTexture=true;
            importer.wrapModeU=TextureWrapMode.Repeat;
            importer.wrapModeV=TextureWrapMode.Clamp;
            importer.anisoLevel=4;
            importer.SaveAndReimport();
            importer=(TextureImporter)AssetImporter.GetAtPath(portrait);
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        BakeIcon(roster[0]);
    }

    static void BakeIcon(RacerDefinition racer)
    {
        Directory.CreateDirectory("Assets/Artwork");
        var portrait=MarbleArt.GeneratePortrait(racer);
        const int size=512;
        var icon=new Texture2D(size,size,TextureFormat.RGBA32,false);
        var pixels=new Color[size*size];
        Color paper=new Color(.97f,.92f,.79f);
        Color ink=new Color(.17f,.28f,.33f);
        for(int y=0;y<size;y++)
        for(int x=0;x<size;x++)
        {
            Vector2 p=new Vector2(x-size*.5f,y-size*.5f);
            float outside=new Vector2(Mathf.Max(0,Mathf.Abs(p.x)-size*.3f),Mathf.Max(0,Mathf.Abs(p.y)-size*.3f)).magnitude-size*.18f;
            Color color=Color.Lerp(paper,new Color(.86f,.81f,.65f),Mathf.Clamp01((size*.5f-y)/size)*.15f);
            float radius=p.magnitude;
            if(Mathf.Abs(radius-size*.4f)<3 && p.y<80) color=ink;
            float u=(x-size*.1f)/(size*.8f),v=(y-size*.1f)/(size*.8f);
            if(u>=0 && u<=1 && v>=0 && v<=1)
            {
                Color marble=portrait.GetPixelBilinear(u,v);
                color=Color.Lerp(color,marble,marble.a);
            }
            color.a=Mathf.Clamp01(1-outside);
            pixels[y*size+x]=color;
        }
        icon.SetPixels(pixels);icon.Apply();
        const string path="Assets/Artwork/MarbleArenaIcon.png";
        File.WriteAllBytes(path,icon.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(icon);
        UnityEngine.Object.DestroyImmediate(portrait);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled=false;
        importer.alphaIsTransparency=true;
        importer.SaveAndReimport();
        PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Standalone,new[]{AssetDatabase.LoadAssetAtPath<Texture2D>(path)},IconKind.Any);
    }

    static void ConfigureAmbientOcclusion()
    {
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PrismRenderer0.asset");
        if(!renderer) return;
        var occlusion=renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
        if(!occlusion)
        {
            occlusion=ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            occlusion.name="Soft contact shading";
            AssetDatabase.AddObjectToAsset(occlusion,renderer);
            renderer.rendererFeatures.Add(occlusion);
        }
        var settings=new SerializedObject(occlusion);
        var source=settings.FindProperty("m_Settings");
        source.FindPropertyRelative("Downsample").boolValue=true;
        source.FindPropertyRelative("Intensity").floatValue=.65f;
        source.FindPropertyRelative("Radius").floatValue=.2f;
        source.FindPropertyRelative("Samples").intValue=2;
        source.FindPropertyRelative("BlurQuality").intValue=1;
        source.FindPropertyRelative("Falloff").floatValue=65;
        settings.ApplyModifiedPropertiesWithoutUndo();
        occlusion.Create();
        EditorUtility.SetDirty(occlusion);
        EditorUtility.SetDirty(renderer);
    }
}
