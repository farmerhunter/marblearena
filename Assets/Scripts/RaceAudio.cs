using UnityEngine;

namespace MarbleArena
{
    public sealed class RaceAudio : MonoBehaviour
    {
        AudioSource music, wind, rolling, ui;
        readonly AudioSource[] impacts = new AudioSource[10];
        readonly AudioClip[] taps = new AudioClip[3];
        readonly AudioClip[] hits = new AudioClip[2];
        int nextImpact, variation;
        bool muted;

        public void Setup()
        {
            music = Source("Music", "race_music", true, .13f);
            wind = Source("Wind", "wind_loop", true, 0);
            rolling = Source("Rolling", "rolling_loop", true, 0);
            ui = Source("UI", null, false, .5f);
            for (int i=0;i<impacts.Length;i++)
            {
                impacts[i] = Source("Spatial glass " + i, null, false, .25f);
                impacts[i].spatialBlend = .85f;
                impacts[i].minDistance = 2;
                impacts[i].maxDistance = 32;
                impacts[i].rolloffMode = AudioRolloffMode.Logarithmic;
                impacts[i].dopplerLevel = .4f;
            }
            for (int i=0;i<taps.Length;i++) taps[i]=Resources.Load<AudioClip>("Audio/glass_tap_"+(i+1));
            for (int i=0;i<hits.Length;i++) hits[i]=Resources.Load<AudioClip>("Audio/glass_hit_"+(i+1));
        }

        AudioSource Source(string name,string clip,bool loop,float volume)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(transform,false);
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = volume;
            if (clip != null)
            {
                source.clip = Resources.Load<AudioClip>("Audio/"+clip);
                source.Play();
            }
            return source;
        }

        public void Play(string name,float volume=1)
        {
            var clip=Resources.Load<AudioClip>("Audio/"+name);
            if(clip) ui.PlayOneShot(clip,volume);
        }

        public void Collision(MarbleRacer racer,float force)
        {
            if(force<1.3f) return;
            var source=impacts[nextImpact++%impacts.Length];
            source.transform.position=racer.transform.position;
            source.pitch=.9f+(variation++%7)*.033f;
            source.volume=Mathf.Clamp(force*.025f,.04f,.25f);
            source.clip=force>5?hits[variation%hits.Length]:taps[variation%taps.Length];
            source.Play();
        }

        public void Tick(float delta,string state,float speed,bool chase)
        {
            bool active=state=="racing";
            float blend=1-Mathf.Exp(-delta*3);
            music.volume=Mathf.Lerp(music.volume,active?.22f:.11f,blend);
            wind.volume=Mathf.Lerp(wind.volume,active?Mathf.InverseLerp(3,22,speed)*(chase?.23f:.055f):0,blend);
            rolling.volume=Mathf.Lerp(rolling.volume,active?Mathf.InverseLerp(0,15,speed)*(chase?.14f:.045f):0,blend);
            rolling.pitch=.7f+Mathf.Clamp(speed/20,0,1.5f);
        }

        public bool ToggleMute()
        {
            muted=!muted;
            AudioListener.volume=muted?0:.8f;
            return muted;
        }
    }
}
