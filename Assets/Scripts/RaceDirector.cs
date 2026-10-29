using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.EventSystems;

namespace MarbleArena
{
    public sealed class RaceDirector : MonoBehaviour
    {
        public ArenaBuilder Arena { get; private set; }
        public RaceCameraRig CameraRig { get; private set; }
        public readonly List<MarbleRacer> Racers = new List<MarbleRacer>();
        public string State { get; private set; } = "menu";
        public float RaceTime { get; private set; }
        public int Seed { get; private set; }
        public RacerDefinition[] Roster { get; private set; }
        public TeamDefinition[] Teams { get; private set; }
        public bool Automated;
        ArenaUI ui;
        RaceAudio audioDirector;
        RacerDefinition[] roster;
        TeamDefinition[] teams;
        MarbleRacer supported, focused;
        readonly List<MarbleRacer> ranking = new List<MarbleRacer>();
        readonly List<MarbleRacer> finishOrder = new List<MarbleRacer>();
        readonly List<float> frameTimes = new List<float>(12000);
        readonly RaceTelemetry telemetry=new RaceTelemetry();
        RaceRow[] finalRows;
        ChampionshipSave save;
        System.Random rng;
        string[] selected;
        string supportedId="ember", notice="", raceId, testReport;
        float countdown, noticeUntil, nextHud, fps, directorClock, firstFinish=-1, testElapsed;
        bool booted, scoreCommitted, dragging, highQuality=true;
        bool testPauseDone, testPauseActive, testPausePassed;
        float testResumeAt, testPauseTime;
        Vector3 testPausePosition;
        int testCaptureStage;
        Vector3 mouseLast;
        int countdownBeat=4, resultFrames;
        float testCaptureAt;
        float testChaseTime,testClosestCameraDistance=1000;
        int testChaseFrames,testCameraNearMarbleFrames,testCameraOffscreenFrames,testCameraOccludedFrames;
        RaceView view = new RaceView();
        readonly int[] points = {15,12,10,8,6,5,4,3,2,1,1,1};

        void Awake() { Boot(); }

        public void Boot(bool withPresentation=true)
        {
            if(booted) return;
            booted=true;
            Application.targetFrameRate=60;
            Application.runInBackground=true;
            QualitySettings.vSyncCount=0;
            Time.fixedDeltaTime=1f/120;
            Time.maximumDeltaTime=.1f;
            Physics.gravity=new Vector3(0,-9.81f,0);
            Physics.simulationMode=SimulationMode.FixedUpdate;
            Physics.defaultSolverIterations=12;
            Physics.defaultSolverVelocityIterations=6;
            roster=JsonUtility.FromJson<RosterFile>("{\"items\":"+Resources.Load<TextAsset>("Data/roster").text+"}").items;
            teams=JsonUtility.FromJson<TeamsFile>("{\"items\":"+Resources.Load<TextAsset>("Data/teams").text+"}").items;
            Roster=roster; Teams=teams;
            selected=roster.Take(8).Select(r=>r.id).ToArray();
            Arena=new GameObject("Midnight Midway · marble race").AddComponent<ArenaBuilder>();
            Arena.Build();
            save=LoadSave();
            if(withPresentation)
            {
                CameraRig=new GameObject("Broadcast cameras").AddComponent<RaceCameraRig>();
                CameraRig.Setup(Arena);
                audioDirector=new GameObject("Original soundscape").AddComponent<RaceAudio>();
                audioDirector.Setup();
                ui=new GameObject("Race interface").AddComponent<ArenaUI>();
                ui.Setup(roster,teams);
                ui.StartRequested+=StartRace;
                ui.CameraRequested+=SetCamera;
                ui.FocusRequested+=Focus;
                ui.ActionRequested+=PlayerAction;
                ui.RestartRequested+=()=>StartRace(selected,supportedId);
                ui.MenuRequested+=ReturnToMenu;
                ui.PauseRequested+=TogglePause;
                ui.MuteRequested+=ToggleMute;
                ui.QualityRequested+=SetQuality;
                ui.SetView("menu");
            }
            ReadArguments();
            if(Automated) StartRace(selected,supportedId);
        }

        void ReadArguments()
        {
            string[] args=Environment.GetCommandLineArgs();
            Automated=args.Contains("--autoplay") || Automated;
            int i=Array.IndexOf(args,"--report");
            if(i>=0 && i+1<args.Length) testReport=args[i+1];
            i=Array.IndexOf(args,"--racers");
            if(i>=0 && i+1<args.Length && int.TryParse(args[i+1],out int count))
                selected=roster.Take(Mathf.Clamp(count,4,12)).Select(r=>r.id).ToArray();
        }

        public void StartRace(string[] ids,string support)
        {
            Time.timeScale=1;
            var valid=ids.Distinct().Where(id=>roster.Any(r=>r.id==id)).ToArray();
            if(valid.Length<4 || valid.Length>12 || !valid.Contains(support)) return;
            selected=valid;
            supportedId=support;
            foreach(var racer in Racers)
            {
                if(!racer) continue;
                if(Application.isPlaying) Destroy(racer.gameObject);
                else DestroyImmediate(racer.gameObject);
            }
            Racers.Clear(); ranking.Clear(); finishOrder.Clear();
            frameTimes.Clear(); finalRows=null;
            testPauseDone=false;testPauseActive=false;testCaptureStage=0;
            testChaseTime=0;testClosestCameraDistance=1000;
            testChaseFrames=testCameraNearMarbleFrames=testCameraOffscreenFrames=testCameraOccludedFrames=0;
            Seed=Environment.TickCount&0x7fffffff;
            string[] args=Environment.GetCommandLineArgs();
            int seedArg=Array.IndexOf(args,"--seed");
            if(seedArg>=0 && seedArg+1<args.Length && int.TryParse(args[seedArg+1],out int argSeed)) Seed=argSeed;
            rng=new System.Random(Seed);
            raceId=Guid.NewGuid().ToString("N");
            scoreCommitted=false;
            RaceTime=0; firstFinish=-1; resultFrames=0;
            int[] positions=Enumerable.Range(0,valid.Length).ToArray();
            for(int i=positions.Length-1;i>0;i--) {int j=rng.Next(i+1);int temp=positions[i];positions[i]=positions[j];positions[j]=temp;}
            Vector3[] starts=Arena.StartPositions(valid.Length);
            for(int i=0;i<valid.Length;i++)
            {
                var entry=roster.First(r=>r.id==valid[i]);
                var racer=new GameObject(entry.name_en).AddComponent<MarbleRacer>();
                racer.transform.position=starts[positions[i]];
                racer.Setup(entry,entry.id==support);
                var closest=Arena.ClosestProgress(racer.transform.position,0);
                racer.Progress=closest.Distance; racer.PathIndex=closest.Index; racer.BestProgress=closest.Distance;
                racer.TrackTangent=closest.Tangent;
                racer.TrackRight=closest.Right;
                racer.LastPhysicsPosition=racer.transform.position;
                racer.LastSafePosition=racer.transform.position;
                racer.AiBoostAt=entry.strategy=="early"?.16f:entry.strategy=="late"?.78f:.45f;
                racer.AiBoostAt+=(float)rng.NextDouble()*.07f-.035f;
                racer.AiNudgeAt=.12f+(float)rng.NextDouble()*.72f;
                racer.AiNudgeDirection=rng.Next(2)==0?-1:1;
                racer.Impact+=(ball,force)=>{if(State=="racing" && audioDirector) audioDirector.Collision(ball,force);};
                racer.PhysicalImpact+=(ball,relativeSpeed)=>{if(State=="racing") telemetry.RecordImpact(ball,relativeSpeed);};
                Racers.Add(racer);
                if(entry.id==support) supported=racer;
            }
            focused=supported;
            telemetry.Reset(Racers);
            countdown=3.6f; countdownBeat=4; directorClock=0;
            SetState("countdown");
            Notice("发车位随机 · 重力驱动 · 左右拨线共享 1 次，推进 1 次",8);
            audioDirector?.Play("select",.5f);
            Debug.Log("RACE_START seed="+Seed+" racers="+valid.Length+" support="+support);
        }

        public void ReleaseRace()
        {
            foreach(var racer in Racers) racer.Body.isKinematic=false;
            SetState("racing");
            RaceTime=0;
            audioDirector?.Play("race_start");
            Notice("GO  ·  两座动力环将在明示窗口内给予额外推进",5);
        }

        void Update()
        {
            if(!booted) return;
            float dt=Time.unscaledDeltaTime;
            fps=Mathf.Lerp(fps,1/Mathf.Max(dt,.001f),1-Mathf.Exp(-dt*2));
            if(State=="racing" && RaceTime>5) frameTimes.Add(dt*1000);
            if(State=="countdown")
            {
                countdown-=dt;
                int beat=Mathf.CeilToInt(countdown);
                if(beat<countdownBeat && beat>0) {countdownBeat=beat;audioDirector?.Play("countdown");}
                if(countdown<=0) ReleaseRace();
            }
            foreach(var racer in Racers) racer.TickVisual(dt,State=="racing");
            Arena.UpdatePulseWindows(RaceTime,Seed);
            HandleInput();
            if(CameraRig && State=="racing" && CameraRig.Mode==0)
            {
                directorClock+=dt;
                if(directorClock>5 && ranking.Count>0)
                {
                    directorClock=0;
                    SelectCompetitionShot();
                }
            }
            audioDirector?.Tick(dt,State,focused?focused.Body.linearVelocity.magnitude:0,CameraRig && CameraRig.Mode==1);
            nextHud-=dt;
            if(nextHud<=0) {nextHud=.1f;RefreshHud();}
            if(Automated)
            {
                testElapsed+=dt;
                if(State=="racing" && RaceTime>14 && !testPauseDone)
                {
                    testPauseDone=true;testPauseActive=true;testPauseTime=RaceTime;
                    testPausePosition=supported.Body.position;
                    testResumeAt=Time.unscaledTime+.7f;
                    TogglePause();
                }
                if(testPauseActive && Time.unscaledTime>=testResumeAt)
                {
                    testPausePassed=Mathf.Abs(RaceTime-testPauseTime)<.001f && Vector3.Distance(testPausePosition,supported.Body.position)<.001f;
                    Debug.Log("TEST_PAUSE "+(testPausePassed?"PASS":"FAIL"));
                    TogglePause();testPauseActive=false;
                }
                if(State=="racing" && supported)
                {
                    if(supported.Progress>Arena.TotalLength*.3f && !supported.NudgeUsed) PlayerAction("nudge_right");
                    if(supported.Progress>Arena.TotalLength*.67f && !supported.BoostUsed) PlayerAction("boost");
                    CaptureAutomaticViews();
                }
                if(State=="results" && resultFrames==3) CaptureTestImage("results");
                if(State=="results" && ++resultFrames>90) {WriteReport();Application.Quit();}
                if(testElapsed>270) {WriteReport();Application.Quit(2);}
            }
        }

        void FixedUpdate() { if(State=="racing") StepRace(Time.fixedDeltaTime); }

        public void StepRace(float dt)
        {
            RaceTime+=dt;
            Arena.TickPhysics(RaceTime,Seed,dt);
            foreach(var racer in Racers)
            {
                if(racer.Finished) continue;
                ProgressSample progress=Arena.ClosestProgress(racer.Body.position,racer.PathIndex);
                if(!progress.Valid || Mathf.Abs(progress.Distance-racer.Progress)>12 || racer.StuckTime>(progress.InBowl?35:12))
                {
                    Debug.Log($"RECOVERY_CAUSE t={RaceTime:F2} id={racer.Definition.id} valid={progress.Valid} sector={progress.Sector} progress={racer.Progress:F2}->{progress.Distance:F2} pos={racer.Body.position:F3} velocity={racer.Body.linearVelocity:F3} stuck={racer.StuckTime:F2}");
                    Recover(racer);
                    continue;
                }
                racer.Progress=progress.Distance; racer.PathIndex=progress.Index;
                racer.TrackTangent=progress.InBowl && racer.Body.linearVelocity.sqrMagnitude>1?racer.Body.linearVelocity.normalized:progress.Tangent;
                racer.TrackRight=progress.InBowl?Vector3.Cross(Vector3.up,racer.TrackTangent).normalized:progress.Right;
                racer.InBowl=progress.InBowl;
                racer.Sector=progress.Sector;
                if(!progress.InBowl) racer.LastSafePosition=progress.Point+Vector3.up*(MarbleRacer.Radius+.12f);
                if(racer.Progress>racer.BestProgress+.2f) {racer.BestProgress=racer.Progress;racer.StuckTime=0;}
                else if(racer.Body.linearVelocity.sqrMagnitude>.16f) racer.StuckTime=0;
                else racer.StuckTime+=dt;
                float fraction=racer.Progress/Arena.TotalLength;
                for(int i=0;i<Mathf.Min(racer.Coils.Length,Arena.PulseDistances.Length);i++)
                {
                    float station=Arena.PulseDistances[i];
                    if(!racer.Coils[i] && racer.Progress>=station)
                    {
                        racer.Coils[i]=true;
                        float phase=Mathf.Repeat(RaceTime*.42f+(Seed%997)*.013f+i*.37f,1);
                        bool lucky=phase<.42f;
                        racer.Impulse(racer.TrackTangent*(lucky?3.1f:1.4f));
                        if(lucky) Notice(racer.Definition.name_zh+" 命中动力窗口  +3.1 m/s",3);
                        if(racer==focused) audioDirector?.Play("pulse",.4f);
                    }
                }
                if(racer!=supported)
                {
                    if(!racer.BoostUsed && fraction>racer.AiBoostAt) ApplyAction(racer,"boost");
                    if(!racer.NudgeUsed && fraction>racer.AiNudgeAt) ApplyAction(racer,racer.AiNudgeDirection<0?"nudge_left":"nudge_right");
                }
                if(Arena.HasCrossedFinish(racer.Body.position,racer.LastPhysicsPosition))
                {
                    TrackSample line=Arena.SampleDistance(Arena.TotalLength-2);
                    float before=Vector3.Dot(racer.LastPhysicsPosition-line.Position,line.Tangent);
                    float after=Vector3.Dot(racer.Body.position-line.Position,line.Tangent);
                    float crossing=Mathf.Clamp01(-before/Mathf.Max(.00001f,after-before));
                    Finish(racer,RaceTime-dt+crossing*dt);
                }
                racer.LastPhysicsPosition=racer.Body.position;
            }
            Reorder();
            telemetry.Step(dt,Racers);
            if(finishOrder.Count==Racers.Count || RaceTime>220 || (firstFinish>=0 && RaceTime>firstFinish+75)) CompleteRace();
        }

        void Recover(MarbleRacer racer)
        {
            TrackSample point=Arena.SampleDistance(Mathf.Max(2,racer.BestProgress));
            racer.Body.position=racer.InBowl?point.Position+Vector3.up*(MarbleRacer.Radius+.15f):racer.LastSafePosition;
            racer.Body.linearVelocity=point.Tangent*1.5f;
            racer.Body.angularVelocity=Vector3.zero;
            racer.Penalty+=2;
            racer.Rescues++;
            racer.StuckTime=0;
            racer.LastPhysicsPosition=racer.Body.position;
            racer.ClearTrail();
            Notice(racer.Definition.name_zh+" 由回收装置送回赛道 · +2 秒",4);
            Debug.Log("RECOVER "+racer.Definition.id+" progress="+racer.Progress.ToString("F1"));
        }

        void Finish(MarbleRacer racer,float crossingTime)
        {
            racer.FinishTime=crossingTime+racer.Penalty;
            finishOrder.Add(racer);
            if(firstFinish<0) firstFinish=RaceTime;
            racer.Body.linearVelocity=Vector3.zero;
            racer.Body.angularVelocity=Vector3.zero;
            racer.Body.isKinematic=true;
            racer.GetComponent<SphereCollider>().enabled=false;
            TrackSample end=Arena.SampleDistance(Arena.TotalLength-1);
            racer.Body.position=end.Position+end.Right*((finishOrder.Count-1)%4-1.5f)*1.15f+Vector3.up*(MarbleRacer.Radius+.04f)-end.Tangent*((finishOrder.Count-1)/4)*1.2f;
            racer.ClearTrail();
            Notice(racer.Definition.name_zh+" 冲线！  "+racer.FinishTime.ToString("F2")+" s",4);
            audioDirector?.Play("glass_tap_1",.5f);
            Debug.Log("FINISH "+racer.Definition.id+" time="+racer.FinishTime.ToString("F3"));
        }

        void Reorder()
        {
            ranking.Clear();ranking.AddRange(Racers);
            ranking.Sort((a,b)=>
            {
                if(a.Finished && b.Finished) return a.FinishTime.CompareTo(b.FinishTime);
                if(a.Finished!=b.Finished) return a.Finished?-1:1;
                return b.Progress.CompareTo(a.Progress);
            });
            for(int i=0;i<ranking.Count;i++) ranking[i].Rank=i+1;
        }

        public void PlayerAction(string action)
        {
            if(State!="racing" || !supported || supported.Finished) return;
            if(ApplyAction(supported,action))
            {
                Notice(action=="boost"?"推进脉冲已释放 · 把握直线！":"拨线已释放 · 本局不能再次改线",3);
                audioDirector?.Play("pulse",.65f);
            }
        }

        bool ApplyAction(MarbleRacer racer,string action)
        {
            if(action=="boost")
            {
                if(racer.BoostUsed) return false;
                racer.BoostUsed=true;
                racer.Impulse(racer.TrackTangent*3.5f);
            }
            else if(action=="nudge_left" || action=="nudge_right")
            {
                if(racer.NudgeUsed) return false;
                racer.NudgeUsed=true;
                racer.Impulse(racer.TrackRight*(action=="nudge_left"?-1.65f:1.65f));
            }
            else return false;
            return true;
        }

        void CompleteRace()
        {
            if(State!="racing") return;
            foreach(var racer in Racers) racer.Body.isKinematic=true;
            Reorder();
            RaceRow[] rows=BuildRows();
            var teamPoints=new Dictionary<string,int>();
            for(int i=0;i<ranking.Count;i++)
            {
                var racer=ranking[i];
                rows[i].points=racer.Finished?points[Mathf.Min(i,points.Length-1)]:0;
                if(!racer.Finished) rows[i].time="DNF";
                string teamId=racer.Definition.team_id;
                if(!teamPoints.ContainsKey(teamId)) teamPoints[teamId]=0;
                teamPoints[teamId]+=rows[i].points;
            }
            if(!scoreCommitted)
            {
                foreach(var row in rows) AddScore(ref save.racers,row.id,row.points);
                foreach(var kv in teamPoints) AddScore(ref save.teams,kv.Key,kv.Value);
                var history=save.races.ToList();
                history.Add(new RaceRecord{race_id=raceId,arena_id="midnight_midway_v1",rules_id="gravity_sprint_v1",utc=DateTime.UtcNow.ToString("o"),seed=Seed,results=rows,team_ids=ranking.Select(r=>r.Definition.team_id).ToArray()});
                save.races=history.TakeLast(100).ToArray();
                scoreCommitted=true;
                if(!Automated) SaveScores();
            }
            foreach(var row in rows) row.total=GetScore(save.racers,row.id);
            finalRows=rows;
            TeamRow[] teamRows=teams.Where(t=>teamPoints.ContainsKey(t.id)).Select(t=>new TeamRow{name=t.name_zh,color=t.primary_color,points=teamPoints[t.id],total=GetScore(save.teams,t.id)}).OrderByDescending(t=>t.points).ToArray();
            SetState("results");
            ui?.ShowResults(rows,teamRows);
            audioDirector?.Play("victory",.7f);
            Debug.Log("RACE_COMPLETE finishers="+finishOrder.Count+" rescues="+Racers.Sum(r=>r.Rescues)+" time="+RaceTime.ToString("F1"));
            if(frameTimes.Count>0)
            {
                float[] sorted=frameTimes.OrderBy(value=>value).ToArray();
                Debug.Log("RENDER_PERFORMANCE frames="+sorted.Length+" average_fps="+(1000f/sorted.Average()).ToString("F1")+
                          " p95_ms="+sorted[Mathf.Min(sorted.Length-1,Mathf.FloorToInt(sorted.Length*.95f))].ToString("F2")+
                          " resolution="+Screen.width+"x"+Screen.height+" gpu="+SystemInfo.graphicsDeviceName+" high="+highQuality);
            }
        }

        RaceRow[] BuildRows()
        {
            return ranking.Select(r=>new RaceRow{id=r.Definition.id,name_zh=r.Definition.name_zh,name_en=r.Definition.name_en,color=r.Definition.primary_color,rank=r.Rank,time=r.Finished?r.FinishTime.ToString("F2")+" s":State=="results"?"DNF":"—",progress=r.Progress/Arena.TotalLength,supported=r.Supported}).ToArray();
        }

        void RefreshHud()
        {
            if(!ui || State=="menu" || State=="results") return;
            Reorder();
            view.time=RaceTime;
            view.countdown=State=="countdown"?Mathf.CeilToInt(countdown).ToString():"";
            view.mode=CameraRig.Mode==0?"自动导演":CameraRig.Mode==1?"后背追逐":"赛场全景";
            view.support_name=supported?supported.Definition.name_zh:"";
            view.speed=focused?focused.Body.linearVelocity.magnitude*3.6f:0;
            view.progress=supported?supported.Progress/Arena.TotalLength:0;
            view.boost_used=supported && supported.BoostUsed;
            view.nudge_used=supported && supported.NudgeUsed;
            view.notice=Time.unscaledTime<noticeUntil?notice:"";
            view.fps=fps;
            view.leaderboard=BuildRows();
            ui.UpdateRace(view);
        }

        void HandleInput()
        {
            if(Input.GetKeyDown(KeyCode.Alpha1)) SetCamera(0);
            if(Input.GetKeyDown(KeyCode.Alpha2)) SetCamera(1);
            if(Input.GetKeyDown(KeyCode.Alpha3)) SetCamera(2);
            if(Input.GetKeyDown(KeyCode.Q)) PlayerAction("nudge_left");
            if(Input.GetKeyDown(KeyCode.E)) PlayerAction("nudge_right");
            if(Input.GetKeyDown(KeyCode.F)) PlayerAction("boost");
            if(Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Space) && State!="menu") TogglePause();
            if(Input.GetKeyDown(KeyCode.M)) ToggleMute();
            if(Input.GetKeyDown(KeyCode.Escape)) {if(State=="paused") TogglePause();else if(State=="racing" || State=="countdown") TogglePause();else ReturnToMenu();}
            if(Input.GetKeyDown(KeyCode.R) && State=="results") StartRace(selected,supportedId);
            if(Input.GetKeyDown(KeyCode.Tab) && Racers.Count>0)
            {
                int index=Racers.IndexOf(focused);
                Focus(Racers[(index+1)%Racers.Count].Definition.id);
            }
            bool cameraInput=(State=="racing" || State=="countdown") && !(EventSystem.current && EventSystem.current.IsPointerOverGameObject());
            if(Input.GetMouseButtonDown(1) && cameraInput) {dragging=true;mouseLast=Input.mousePosition;}
            if(Input.GetMouseButtonUp(1)) dragging=false;
            if(dragging && CameraRig)
            {
                Vector3 delta=Input.mousePosition-mouseLast;
                CameraRig.Orbit(delta.x,delta.y);mouseLast=Input.mousePosition;
            }
            if(CameraRig && cameraInput && Mathf.Abs(Input.mouseScrollDelta.y)>.01f) CameraRig.Zoom(-Input.mouseScrollDelta.y);
        }

        void LateUpdate()
        {
            if(CameraRig)
            {
                CameraRig.UpdateCamera(Time.unscaledDeltaTime,State,focused,Racers);
                CameraRig.UpdateShowLighting(RaceTime,focused?focused.Progress/Arena.TotalLength:0,State);
                if(Automated && State=="racing" && CameraRig.Mode==1 && focused && !focused.Finished)
                {
                    testChaseTime+=Time.unscaledDeltaTime;
                    if(testChaseTime<1) return;
                    testChaseFrames++;
                    Vector3 eye=CameraRig.Camera.transform.position;
                    float closest=1000;
                    foreach(var racer in Racers) if(!racer.Finished) closest=Mathf.Min(closest,Vector3.Distance(eye,racer.transform.position));
                    testClosestCameraDistance=Mathf.Min(testClosestCameraDistance,closest);
                    if(closest<MarbleRacer.Radius+.3f) testCameraNearMarbleFrames++;
                    Vector3 screen=CameraRig.Camera.WorldToViewportPoint(focused.transform.position);
                    if(screen.z<=0 || screen.x<-.05f || screen.x>1.05f || screen.y<-.05f || screen.y>1.05f) testCameraOffscreenFrames++;
                    if(Physics.Linecast(focused.transform.position,eye,1<<8)) testCameraOccludedFrames++;
                }
                else testChaseTime=0;
            }
        }

        void SelectCompetitionShot()
        {
            MarbleRacer best=focused;
            float bestScore=-1;
            foreach(var candidate in Racers)
            {
                if(candidate.Finished) continue;
                float score=candidate.Supported?.75f:0;
                foreach(var other in Racers)
                {
                    if(other==candidate || other.Finished) continue;
                    Vector3 separation=other.Body.position-candidate.Body.position;
                    if(Mathf.Abs(separation.y)<2.4f && separation.sqrMagnitude<49) score+=Mathf.Lerp(2.2f,.5f,Mathf.Sqrt(separation.sqrMagnitude)/7);
                }
                if(candidate.InBowl) score+=1;
                score*=Mathf.Lerp(.3f,1f,Mathf.Clamp01(candidate.Body.linearVelocity.magnitude/5));
                score+=Mathf.Min(candidate.Body.linearVelocity.magnitude,12)*.12f;
                if(candidate==focused) score+=.8f;
                if(candidate.Rank==1) score+=.6f;
                if(score>bestScore) {bestScore=score;best=candidate;}
            }
            focused=best?best:supported;
        }

        public void SetCamera(int mode) {CameraRig?.SetMode(mode);directorClock=0;Debug.Log("CAMERA_MODE "+mode);}
        void Focus(string id) {focused=Racers.FirstOrDefault(r=>r.Definition.id==id)??supported;directorClock=-10;Notice("正在跟随 "+focused.Definition.name_zh,3);}
        string pausedFrom="racing";
        public void TogglePause()
        {
            if(State=="paused") {Time.timeScale=1;SetState(pausedFrom);}
            else if(State=="racing" || State=="countdown") {pausedFrom=State;Time.timeScale=0;SetState("paused");}
        }
        void ToggleMute()
        {
            if(!audioDirector) return;
            bool muted=audioDirector.ToggleMute();
            ui?.SetSoundMuted(muted);
            Notice(muted?"声音已关闭":"声音已开启",2);
        }
        void SetQuality(bool enabled)
        {
            highQuality=enabled;
            CameraRig?.SetQuality(enabled);
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline) {pipeline.renderScale=enabled?.75f:.5f;pipeline.msaaSampleCount=enabled?4:2;pipeline.shadowDistance=enabled?55:30;}
            Notice(enabled?"精致画质":"流畅画质",2);
        }
        void SetState(string value) {State=value;ui?.SetView(value);}
        void Notice(string text,float duration) {notice=text;noticeUntil=Time.unscaledTime+duration;}
        void ReturnToMenu()
        {
            Time.timeScale=1;
            foreach(var racer in Racers) racer.Body.isKinematic=true;
            SetState("menu");
        }

        ChampionshipSave LoadSave()
        {
            try
            {
                string file=Path.Combine(Application.persistentDataPath,"championship-v1.json");
                if(File.Exists(file)) return JsonUtility.FromJson<ChampionshipSave>(File.ReadAllText(file))??new ChampionshipSave();
            }
            catch(Exception error) {Debug.LogWarning("Score history unavailable: "+error.Message);}
            return new ChampionshipSave();
        }
        void SaveScores()
        {
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(Path.Combine(Application.persistentDataPath,"championship-v1.json"),JsonUtility.ToJson(save,true));
            }
            catch(Exception error) {Notice("本局已结算，历史积分未能保存",8);Debug.LogWarning(error.Message);}
        }
        static void AddScore(ref ScoreEntry[] scores,string id,int count)
        {
            ScoreEntry entry=scores.FirstOrDefault(s=>s.id==id);
            if(entry==null) {entry=new ScoreEntry{id=id};scores=scores.Append(entry).ToArray();}
            entry.points+=count;
        }
        static int GetScore(ScoreEntry[] scores,string id) => scores.FirstOrDefault(s=>s.id==id)?.points??0;

        [Serializable] class RunReport
        {
            public string state,device,execution_context;
            public int seed,finishers,rescues;
            public float time,fps;
            public float average_fps,p95_frame_ms;
            public int width,height;
            public bool boost_used,nudge_used,pause_passed;
            public RaceRow[] rows;
            public RaceTelemetrySummary telemetry;
            public int chase_frames,camera_near_marble_frames,camera_target_offscreen_frames,camera_track_occluded_frames;
            public float closest_camera_marble_distance;
        }
        public void WriteReport(string path=null)
        {
            path=path??testReport;
            if(string.IsNullOrEmpty(path)) return;
            var report=new RunReport{state=State,device=SystemInfo.graphicsDeviceName,seed=Seed,finishers=finishOrder.Count,rescues=Racers.Sum(r=>r.Rescues),time=RaceTime,fps=fps,boost_used=supported&&supported.BoostUsed,nudge_used=supported&&supported.NudgeUsed,rows=finalRows??BuildRows()};
            report.width=Screen.width;report.height=Screen.height;report.pause_passed=testPausePassed;
            report.execution_context=Application.isEditor?"editor_physics_trial":"native_player";
            report.telemetry=telemetry.Summary();
            report.chase_frames=testChaseFrames;report.camera_near_marble_frames=testCameraNearMarbleFrames;
            report.camera_target_offscreen_frames=testCameraOffscreenFrames;report.camera_track_occluded_frames=testCameraOccludedFrames;
            report.closest_camera_marble_distance=testChaseFrames>0?testClosestCameraDistance:0;
            if(frameTimes.Count>0)
            {
                float[] ordered=frameTimes.OrderBy(t=>t).ToArray();
                report.average_fps=1000f/ordered.Average();
                report.p95_frame_ms=ordered[Mathf.Min(ordered.Length-1,Mathf.FloorToInt(ordered.Length*.95f))];
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
        }
        void OnApplicationQuit() {Time.timeScale=1;}

        void CaptureAutomaticViews()
        {
            if(!CameraRig || string.IsNullOrEmpty(testReport) || testCaptureStage>=12) return;
            float[] moments={2.5f,8,25,31,48,65};
            int[] modes={1,0,0,1,2,1};
            int shot=testCaptureStage/2;
            if(testCaptureStage%2==0 && RaceTime>moments[shot])
            {
                if(shot==2)
                {
                    var bowlRacer=Racers.FirstOrDefault(r=>!r.Finished && r.Sector=="funnel" && r.Body.position.y<17);
                    if(!bowlRacer && RaceTime<38) return;
                    if(bowlRacer) focused=bowlRacer;
                }
                SetCamera(modes[shot]);testCaptureAt=RaceTime;testCaptureStage++;
            }
            else if(testCaptureStage%2==1 && RaceTime>testCaptureAt+1)
            {
                CaptureTestImage(shot+"-"+(modes[shot]==0?"director":modes[shot]==1?"chase":"park"));
                testCaptureStage++;
            }
        }

        void CaptureTestImage(string suffix)
        {
            if(string.IsNullOrEmpty(testReport)) return;
            string destination=Path.Combine(Path.GetDirectoryName(testReport),Path.GetFileNameWithoutExtension(testReport)+"-"+suffix+".png");
            ScreenCapture.CaptureScreenshot(destination);
        }
    }
}
