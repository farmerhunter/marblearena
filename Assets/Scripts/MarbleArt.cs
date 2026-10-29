using System.Collections.Generic;
using UnityEngine;

namespace MarbleArena
{
    public static class MarbleArt
    {
        static readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
        static readonly Dictionary<string,Sprite> portraits=new Dictionary<string,Sprite>();
        static readonly Color Cream=new Color(.96f,.91f,.78f);

        public static bool IsGlass(RacerDefinition racer) => racer.marble_pattern=="cat_eye" || racer.marble_pattern=="honey";

        public static Texture2D SurfaceTexture(RacerDefinition racer)
        {
            if(textures.TryGetValue(racer.id,out var existing)) return existing;
            var saved=Resources.Load<Texture2D>("MarbleTextures/"+racer.id);
            if(saved) {textures[racer.id]=saved;return saved;}
            var texture=GenerateSurface(racer);
            textures[racer.id]=texture;
            return texture;
        }

        public static Texture2D GenerateSurface(RacerDefinition racer)
        {
            const int width=512,height=256;
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,true){name=racer.id+" hand-poured pattern",wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp,anisoLevel=4};
            var pixels=new Color[width*height];
            for(int y=0;y<height;y++)
            for(int x=0;x<width;x++)
            {
                float latitude=(y+.5f)/height*Mathf.PI;
                float longitude=(x+.5f)/width*Mathf.PI*2;
                Vector3 point=new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));
                pixels[y*width+x]=Pigment(racer,point);
            }
            texture.SetPixels(pixels);texture.Apply(true,false);
            return texture;
        }

        public static Sprite Portrait(RacerDefinition racer)
        {
            if(portraits.TryGetValue(racer.id,out var cached)) return cached;
            var texture=GeneratePortrait(racer);
            var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
            sprite.name=racer.id+" collectible portrait";
            portraits[racer.id]=sprite;
            return sprite;
        }

        public static Texture2D GeneratePortrait(RacerDefinition racer)
        {
            const int size=256;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name=racer.id+" portrait",wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[size*size];
            Vector3 light=new Vector3(-.5f,.7f,1).normalized;
            Vector3 half=(light+Vector3.forward).normalized;
            Quaternion turn=Quaternion.Euler(18,28,-12);
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float px=(x+.5f-size*.5f)/(size*.45f),py=(y+.5f-size*.51f)/(size*.45f);
                float r=px*px+py*py;
                if(r>1)
                {
                    float shadow=Mathf.Exp(-(px*px*4+Mathf.Pow((py+.88f)*6,2)))*.14f;
                    pixels[y*size+x]=new Color(.24f,.22f,.19f,shadow);
                    continue;
                }
                Vector3 normal=new Vector3(px,py,Mathf.Sqrt(1-r));
                Color pigment=Pigment(racer,turn*normal);
                float diffuse=.48f+.48f*Mathf.Max(0,Vector3.Dot(normal,light));
                float softSheen=Mathf.Pow(Mathf.Max(0,Vector3.Dot(normal,half)),65)*.45f;
                float rim=Mathf.Pow(1-normal.z,3);
                Color color=pigment*diffuse*(1-rim*.14f)+Color.white*softSheen;
                if(IsGlass(racer)) color=Color.Lerp(color,new Color(.91f,.95f,.87f),Mathf.Pow(Mathf.Max(0,normal.y),6)*.22f);
                color.a=Mathf.Clamp01((1-Mathf.Sqrt(r))*size*.45f);
                pixels[y*size+x]=color;
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }

        public static Color Pigment(RacerDefinition racer,Vector3 p)
        {
            Color primary=racer.Color,accent=racer.Accent;
            float noise=Noise(p*2.1f+Vector3.one*.7f);
            float fine=Noise(p*11+Vector3.one*2.4f);
            float angle=Mathf.Atan2(p.z,p.x);
            float ribbon=Mathf.Sin(p.y*7+angle*2.0f+noise*6+p.x*1.5f);
            Color result;
            switch(racer.marble_pattern)
            {
                case "cat_eye":
                case "honey":
                    float eye=Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs(p.x*Mathf.Cos(p.y*3)+p.z*Mathf.Sin(p.y*3))*5),.65f);
                    result=Color.Lerp(primary,accent,eye*.95f);
                    break;
                case "speckle":
                    float spot=Mathf.Pow(Mathf.Clamp01((Noise(p*36+Vector3.one*4)-.53f)*6),2);
                    result=Color.Lerp(primary,accent,spot*.85f);
                    result*=.96f+fine*.08f;
                    break;
                case "porcelain":
                    float flower=Mathf.Sin(p.y*14+noise*2)*Mathf.Sin(angle*9+noise*2);
                    float blue=Smooth(.15f,.45f,flower);
                    result=Color.Lerp(accent,primary,blue);
                    break;
                case "jade":
                    float vein=Mathf.Pow(1-Mathf.Abs(Mathf.Sin(p.x*7+p.y*5+noise*13)),5);
                    result=Color.Lerp(primary,accent,.18f+noise*.35f);
                    result=Color.Lerp(result,primary*.48f,vein*.6f);
                    break;
                case "ink":
                    float ink=Smooth(.42f,.61f,Noise(p*2.7f+new Vector3(noise*2,noise,0)));
                    result=Color.Lerp(accent,primary,ink);
                    break;
                case "agate":
                    float band=Mathf.Sin(p.y*25+noise*10+p.x*2)*.5f+.5f;
                    result=Color.Lerp(primary,accent,Mathf.Pow(band,.65f));
                    result=Color.Lerp(result,primary*.65f,Mathf.Pow(1-band,16)*.45f);
                    break;
                case "pinwheel":
                    float wheel=Mathf.Repeat((angle+p.y*2.5f+noise*.6f)/Mathf.PI*2,3);
                    result=wheel<1?primary:wheel<2?accent:new Color(.77f,.29f,.23f);
                    result=Color.Lerp(result,Cream,Mathf.Pow(Mathf.Abs(p.y),7)*.35f);
                    break;
                case "pearl":
                    result=Color.Lerp(primary,accent,.45f+.45f*Mathf.Sin(p.y*5+noise*9));
                    break;
                case "ribbon":
                    result=Color.Lerp(accent,primary,Smooth(-.2f,.18f,ribbon));
                    break;
                case "jam":
                    result=Color.Lerp(accent,primary,Smooth(-.35f,.5f,ribbon+fine*.24f));
                    break;
                default:
                    result=Color.Lerp(accent,primary,Smooth(-.35f,.28f,ribbon));
                    break;
            }
            result*=.975f+fine*.05f;
            result.a=1;
            return result;
        }

        static float Smooth(float low,float high,float value)
        {
            float t=Mathf.Clamp01((value-low)/(high-low));
            return t*t*(3-2*t);
        }

        static float Noise(Vector3 p)
        {
            return (Mathf.PerlinNoise(p.x+.31f,p.y+2.13f)+Mathf.PerlinNoise(p.y+1.7f,p.z+6.2f)+Mathf.PerlinNoise(p.z+7.1f,p.x+3.7f))/3;
        }
    }
}
