using System; using System.IO; using System.Text; using System.Globalization; using UnityEngine; using HorseRace.EditorTools;
class P {
  static string F(float v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
  static string Pl(Placement q)=>"{\"a\":\""+q.asset+"\",\"p\":"+(q.part==null?"null":"\""+q.part+"\"")+",\"pos\":"+V(q.position)+",\"yaw\":"+F(q.yawDeg)+",\"roll\":"+F(q.rollDeg)+",\"s\":"+F(q.scale)+"}";
  // Build World の見どころ・ゴール・スタートの素材（SetPieceBuilder / CourseBuilder と同じ置き方の近似。下見用）
  static string X(string a,string p,Vector3 pos,float yaw,float s,float roll=0f,Vector3? sv=null)=>"{\"a\":\""+a+"\",\"p\":"+(p==null?"null":"\""+p+"\"")+",\"pos\":"+V(pos)+",\"yaw\":"+F(yaw)+",\"roll\":"+F(roll)+",\"s\":"+F(s)+(sv.HasValue?",\"sv\":"+V(sv.Value):"")+"}";
  static float G(float[,] h,float x,float z)=>WorldLayout.SampleHeight(h,StageDefinitions.SIZE,HeightmapGen.HEIGHT_M,x,z);
  static Vector3 GP(float[,] h,Vector3 p)=>new Vector3(p.x,G(h,p.x,p.z),p.z);
  static string Extras(StageDef d,Vector3[] local,float[,] h0,float[,] h,float goal,SetPieces.ShortcutPlan sc){
    var l=new System.Collections.Generic.List<string>();
    const string DP="Packs/DesertProps/DesertProps.fbx",FP="Packs/FarmProps/FarmProps.fbx",HB="Packs/HayBales/HayBales.fbx",DR="Packs/DesertRocks/DesertRocks.fbx",WL="Packs/WoodLogs/WoodLogs.fbx";
    const string CL="Packs/Cliffs/Cliffs.fbx",WP="Packs/WantedPosters/WantedPosters.fbx";
    float hw0(float a)=>StageDefinitions.HalfWidthAt(d,a);
    // スタートの門（柵 9 枚）と両端の街灯・干し草
    {float a=StageDefinitions.START_ARC_M+3f; Vector3 t=WorldLayout.TangentAtArc(local,a),r=WorldLayout.RightOf(t),c=WorldLayout.PointAtArc(local,a); float yaw=WorldLayout.YawDeg(t); float half=9*2.74f*0.5f;
     for(int i=0;i<9;i++) l.Add(X(FP,"FarmProps_03",GP(h,c+r*(-half+(i+0.5f)*2.74f)),yaw,1f));
     for(int s=-1;s<=1;s+=2){ l.Add(X(FP,"FarmProps_02",GP(h,c+r*(s*(half+1.2f))),yaw,1f)); l.Add(X(HB,"HayBales_01",GP(h,c+r*(s*(half+3f))-t*0.8f),yaw,1f)); }}
    // ゴールゲート（立てた丸太・横木・街灯・干し草）
    {Vector3 t=WorldLayout.TangentAtArc(local,goal),r=WorldLayout.RightOf(t),c=GP(h,WorldLayout.PointAtArc(local,goal)); float yaw=WorldLayout.YawDeg(t);
     for(int s=-1;s<=1;s+=2){ l.Add(X(WL,"WoodLogs_01",GP(h,c+r*(s*CourseGeometry.GATE_HALF))+Vector3.up*3f,yaw,1f,90f,new Vector3(6.6f/1.5f,2.2f,2.2f))); l.Add(X(FP,"FarmProps_02",GP(h,c+r*(s*(CourseGeometry.GATE_HALF+1.4f))-t*0.6f),yaw+(s>0?90f:-90f),1.25f));
       Vector3 foot=c+r*(s*(CourseGeometry.GATE_HALF+1.2f))+t*1.6f; l.Add(X(HB,"HayBales_01",GP(h,foot),yaw+s*20f,1f)); l.Add(X(HB,"HayBales_01",GP(h,foot+r*(s*1.6f)),yaw-s*30f,0.9f)); }
     l.Add(X(WL,"WoodLogs_03",c+Vector3.up*6.1f,yaw,1f,0f,new Vector3((CourseGeometry.GATE_HALF*2f+1f)/1.2f,2.2f,2.2f)));}
    // 全力の合図（街灯 2 本と札の柱）
    {float a=StageDefinitions.DashArc(goal); Vector3 t=WorldLayout.TangentAtArc(local,a),r=WorldLayout.RightOf(t),c=WorldLayout.PointAtArc(local,a); float yaw=WorldLayout.YawDeg(t), w=hw0(a);
     for(int s=-1;s<=1;s+=2) l.Add(X(FP,"FarmProps_02",GP(h,c+r*(s*(w+CourseMarks.DASH_LAMP_OUT_M))),yaw,1.2f));
     l.Add(X(WL,"WoodLogs_01",GP(h,c+r*(w+CourseMarks.DASH_SIGN_OUT_M)+t*3f)+Vector3.up*1.3f,yaw,1f,90f,new Vector3(2.6f/1.5f,1.1f,1.1f)));}
    // 岩の橋（地形の背）：縁の岩・背の両脇の崖・街灯
    float br=SetPieces.BridgeArc(d,goal);
    if(br>=0f){ Vector3 t=WorldLayout.TangentAtArc(local,br),r=WorldLayout.RightOf(t),c=GP(h,WorldLayout.PointAtArc(local,br)); float yaw=WorldLayout.YawDeg(t); float lip=SetPieces.TRENCH_HALF_WIDTH+SetPieces.TRENCH_BLEND+0.8f; int gi=0;
      for(int e=-1;e<=1;e+=2) for(int s=-1;s<=1;s+=2) for(float la=SetPieces.SPINE_HALF+2.5f;la<SetPieces.DETOUR_FROM-2f;la+=4.2f,gi++) l.Add(X(DR,"DesertRocks_0"+(4+gi%6),GP(h,c+t*(e*lip)+r*(s*la))-Vector3.up*0.3f,yaw+90f+gi*37f,0.9f));
      for(int s=-1;s<=1;s+=2) for(int e=-1;e<=1;e+=2){ int k=(s+1)+(e+1)/2; l.Add(X(CL,"Cliffs_1"+k,GP(h,c+r*(s*(SetPieces.SPINE_HALF+3f))+t*(e*3.5f))-Vector3.up,yaw+(s>0?0f:180f),0.35f+0.05f*k)); }
      float len=(SetPieces.TRENCH_HALF_WIDTH+SetPieces.TRENCH_BLEND)*2f;
      for(int e=-1;e<=1;e+=2) for(int s=-1;s<=1;s+=2) l.Add(X(FP,"FarmProps_02",GP(h,c+t*(e*(len*0.5f+3f))+r*(s*(SetPieces.SPINE_HALF+1.5f))),yaw+(s>0?-90f:90f),1f)); }
    // 踏み切り（地形）：杭の列・干し草・骨
    float jp=SetPieces.JumpArc(d,goal);
    if(jp>=0f){ Vector3 t=WorldLayout.TangentAtArc(local,jp),r=WorldLayout.RightOf(t),lp=GP(h,WorldLayout.PointAtArc(local,jp)); float yaw=WorldLayout.YawDeg(t);
      for(int s=-1;s<=1;s+=2){ for(float a=-SetPieces.JUMP_LENGTH;a<=0f;a+=5.5f) l.Add(X(DP,"DesertProps_10",GP(h,lp+t*a+r*(s*(SetPieces.KICKER_HALF_WIDTH+1.5f))),yaw+90f,1f)); Vector3 hp=GP(h,lp+r*(s*9f)-t*0.5f); l.Add(X(HB,"HayBales_01",hp,yaw,1f)); l.Add(X(HB,"HayBales_01",hp+Vector3.up*1.55f,yaw+40f,0.9f)); }
      l.Add(X("SkeletonHorse.fbx",null,GP(h,lp+t*22f+r*18f)-Vector3.up*0.15f,yaw+30f,1f,85f));
      for(int i=0;i<3;i++) l.Add(X(DP,i==0?"DesertProps_07":"DesertProps_08",GP(h,lp+t*(16f+i*6f)-r*(16f+i)),yaw+i*70f,1.1f)); }
    // 近道の入口の街灯と札の柱
    if(sc!=null){ Vector3 dir=new Vector3(sc.b.x-sc.a.x,0f,sc.b.z-sc.a.z).normalized,r=WorldLayout.RightOf(dir); Vector3 e0=Vector3.Lerp(sc.a,sc.b,sc.f0); float yaw=WorldLayout.YawDeg(dir);
      for(int s=-1;s<=1;s+=2) l.Add(X(FP,"FarmProps_02",GP(h,e0+r*(s*(SetPieces.SHORTCUT_HALF_WIDTH+1.5f))),yaw,1f));
      l.Add(X(WL,"WoodLogs_01",GP(h,e0+r*(SetPieces.SHORTCUT_HALF_WIDTH+3.5f)-dir*2f)+Vector3.up*1.3f,yaw,1f,90f,new Vector3(2.6f/1.5f,1.1f,1.1f))); }
    return string.Join(",",l);
  }
  static readonly float[][,] HS=new float[3][,];
  static void Main(string[] a){
    string outDir=a[0]; var js=new StringBuilder("{\"stages\":[");
    foreach(var d in StageDefinitions.All){
      var local=StageDefinitions.SampleLocal(d); float goal=StageDefinitions.GoalArc(d,local);
      var norm=StageDefinitions.Normalize(local); var h=HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES, d,norm);
      HeightmapGen.ApplyRim(h,StageDefinitions.SIZE,HeightmapGen.HEIGHT_M,HeightmapGen.RIM_WIDTH_M,HeightmapGen.RIM_MIN_M,HeightmapGen.RIM_MAX_M,d.seed,norm);
      var h0=(float[,])h.Clone();
      var plan=SetPieces.ApplyTerrain(d,local,h,goal);
      HS[d.index]=h;
      var sand=HeightmapGen.SandMask(StageDefinitions.ALPHAMAP_RES,d.kind,d.seed,d.kind==HeightmapGen.KIND_CANYON?h:null);
      int n=h.GetLength(0);
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"h{d.index}.bin")))){ bw.Write(n); for(int z=0;z<n;z++)for(int x=0;x<n;x++)bw.Write(h[z,x]); }
      int m=sand.GetLength(0);
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"s{d.index}.bin")))){ bw.Write(m); for(int z=0;z<m;z++)for(int x=0;x<m;x++)bw.Write(sand[z,x]); }
      var ch=SetPieces.WithHeights(local,h);
      var sbC=new StringBuilder(); for(int i=0;i<ch.Length;i+=5){ if(sbC.Length>0)sbC.Append(','); sbC.Append(V(ch[i])); }
      var cps=WorldLayout.CheckpointArcs(goal,StageDefinitions.CP_SPACING_M);
      var sbP=new StringBuilder(); foreach(var c in cps){ if(sbP.Length>0)sbP.Append(','); var p=WorldLayout.PointAtArc(ch,c); sbP.Append(V(p)); }
      var sbR=new StringBuilder(); foreach(var rs in SetPieces.RelicSpots(d,local,goal,plan)){ if(sbR.Length>0)sbR.Append(','); sbR.Append(V(GP(h,rs.local)+Vector3.up*rs.up)); }
      float br=SetPieces.BridgeArc(d,goal), jp=SetPieces.JumpArc(d,goal);
      string bridge= br<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,br))+",\"t\":"+V(WorldLayout.TangentAtArc(local,br))+"}";
      string jump= jp<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,jp))+",\"t\":"+V(WorldLayout.TangentAtArc(local,jp))+"}";
      string sc= plan==null? "null" : "{\"a\":"+V(plan.a)+",\"b\":"+V(plan.b)+",\"drop\":"+F(plan.drop)+"}";
      var dp=DressingPlan.For(d,local,h,goal,plan);
      var sbD=new StringBuilder(); foreach(var q in dp){ if(sbD.Length>0)sbD.Append(','); sbD.Append(Pl(q)); }
      if(d.index>0) js.Append(',');
      float ps=SetPieces.PassArc(d,goal);
      string pass= ps<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,ps))+",\"t\":"+V(WorldLayout.TangentAtArc(local,ps))+"}";
      string lm="null"; if(DressingPlan.LandmarkSpot(d,local,goal,out Vector3 lmp,out float lma)) lm=V(GP(h,lmp));
      js.Append("{\"index\":"+d.index+",\"label\":\""+d.label+"\",\"center\":"+V(d.center)+",\"goal\":"+F(goal)+",\"course\":["+sbC+"],\"cps\":["+sbP+"],\"relics\":["+sbR+"],\"bridge\":"+bridge+",\"jump\":"+jump+",\"pass\":"+pass+",\"landmark\":"+lm+",\"shortcut\":"+sc+",\"dress\":["+sbD+"],\"extras\":["+Extras(d,local,h0,h,goal,plan)+"]}");
      Console.WriteLine($"stage {d.index} {d.label} goal {goal:0} m, cps {cps.Length}, dress {dp.Count}");
    }
    js.Append("],\"lobby\":[");
    var tn=LobbyLayout.TOWN_NOMINAL_RECT;
    var lp=DressingPlan.ForLobby(new Vector2(tn.x,tn.y),new Vector2(tn.z,tn.w),LobbyLayout.LobbyDressingExclusions(),4242);
    var sbL=new StringBuilder(); foreach(var q in lp){ if(sbL.Length>0)sbL.Append(','); sbL.Append(Pl(q)); } js.Append(sbL);
    js.Append("],\"backdrop\":[");
    var bp=DressingPlan.Backdrop(7);
    var sbB=new StringBuilder(); foreach(var q in bp){ if(sbB.Length>0)sbB.Append(','); sbB.Append(Pl(q)); } js.Append(sbB);
    js.Append("],\"fenceRect\":["+F(LobbyLayout.FENCE_RECT.x)+","+F(LobbyLayout.FENCE_RECT.y)+","+F(LobbyLayout.FENCE_RECT.z)+","+F(LobbyLayout.FENCE_RECT.w)+"],\"groundRect\":["+F(LobbyLayout.GROUND_RECT.x)+","+F(LobbyLayout.GROUND_RECT.y)+","+F(LobbyLayout.GROUND_RECT.z)+","+F(LobbyLayout.GROUND_RECT.w)+"],\"practiceRect\":["+F(LobbyLayout.PRACTICE_RECT.x)+","+F(LobbyLayout.PRACTICE_RECT.y)+","+F(LobbyLayout.PRACTICE_RECT.z)+","+F(LobbyLayout.PRACTICE_RECT.w)+"]"
      +",\"townBackZ\":"+F(LobbyLayout.TOWN_BACK_Z)+",\"stable\":"+V(LobbyLayout.STABLE)+",\"jump\":"+V(LobbyLayout.PRACTICE_JUMP)+",\"bump\":"+V(LobbyLayout.PRACTICE_BUMP));
    js.Append(",\"spots\":[");
    var sbS=new StringBuilder(); foreach(var kv in LobbyLayout.FunctionalSpots()){ if(sbS.Length>0)sbS.Append(','); sbS.Append("{\"n\":\""+kv.Key+"\",\"x\":"+F(kv.Value.x)+",\"z\":"+F(kv.Value.z)+",\"r\":"+F(kv.Value.w)+"}"); } js.Append(sbS); js.Append("]");
    js.Append(",\"fence\":[");
    var segs=LobbyLayout.FenceSegments(LobbyLayout.FENCE_RECT,LobbyLayout.Gates,LobbyLayout.FENCE_PIECE_M);
    var sbF=new StringBuilder(); foreach(var sg in segs){ if(sbF.Length>0)sbF.Append(','); sbF.Append("["+V(sg[0])+","+V(sg[1])+"]"); } js.Append(sbF);
    // ロビーの地面（LobbyBuilder と同じ式：柵の外の盛り上がり＋尾根のノイズ、町の足元は 1m 下げる、練習場の丘）を 1m 格子で書き出す
    var GR=LobbyLayout.GROUND_RECT; int lnx=(int)(GR.z-GR.x)+1, lnz=(int)(GR.w-GR.y)+1;
    var lh=new float[lnz,lnx];
    using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,"lobby.bin")))){ bw.Write(lnx); bw.Write(lnz);
      for(int z=0;z<lnz;z++)for(int x=0;x<lnx;x++){ var rel=new Vector3(GR.x+x,0f,GR.y+z); float noise=Mathf.Clamp01(0.8f*HeightmapGen.Noise01(x/140f,z/140f,99,21)+0.2f*HeightmapGen.Noise01(x/35f,z/35f,99,22));
        float v=LobbyLayout.GroundHeight(LobbyLayout.FENCE_RECT,rel,noise); var tn2=LobbyLayout.TOWN_NOMINAL_RECT; if(rel.x>tn2.x+1f&&rel.x<tn2.z-1f&&rel.z>tn2.y+1f&&rel.z<tn2.w-1f) v=-1f;
        float db=WorldLayout.DistXZ(rel,LobbyLayout.PRACTICE_BUMP); if(db<6f) v=Mathf.Max(v,0.45f-1.45f*Mathf.Clamp01((db-1.5f)/4.5f));
        lh[z,x]=v; bw.Write(v);} }
    float LH(Vector3 rel){ int x=Mathf.Clamp((int)Mathf.Round(rel.x-GR.x),0,lnx-1), z=Mathf.Clamp((int)Mathf.Round(rel.z-GR.y),0,lnz-1); return lh[z,x]; }
    // 外の地面（TerrainBuilder.BuildFiller と同じ考え方：基本の高さ、遊べる範囲の縁から 150m で縁の高さへ合わせる）
    var FR=WorldBounds.FillerRect(); const int FN=257;
    using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,"filler.bin")))){ bw.Write(FN); bw.Write(FR.x); bw.Write(FR.y); bw.Write(FR.z); bw.Write(FR.w);
      var rects=WorldBounds.PlayableRects();
      for(int z=0;z<FN;z++)for(int x=0;x<FN;x++){ float wx=FR.x+(FR.z-FR.x)*x/(FN-1f), wz=FR.y+(FR.w-FR.y)*z/(FN-1f); float y=WorldBounds.FILLER_Y+WorldBounds.FillerBaseHeight(wx,wz);
        int best=-1; float bd=float.MaxValue; var wp=new Vector3(wx,0f,wz); for(int i=0;i<rects.Length;i++){ float dd=WorldBounds.DistToRect(wp,rects[i]); if(dd<bd){bd=dd;best=i;} }
        if(best>=0&&bd<150f){ var r=rects[best]; float qx=Mathf.Clamp(wx,r.x+2f,r.z-2f), qz=Mathf.Clamp(wz,r.y+2f,r.w-2f); float ey;
          if(best<3){ var o=StageDefinitions.All[best].Origin; ey=G(HS[best],qx-o.x,qz-o.z);} else { var lc=WorldBounds.LOBBY_CENTER; ey=LH(new Vector3(qx-lc.x,0f,qz-lc.z)); }
          float target=bd<=0f?ey-3f:ey-0.8f; float u=Mathf.Clamp01(bd/150f); y=Mathf.Lerp(target,y,u*u*(3f-2f*u)); }
        bw.Write(y);} }
    js.Append("],\"crown\":[");
    var crown=LobbyLayout.RimCrown(LobbyLayout.FENCE_RECT,11);
    var sbK=new StringBuilder(); foreach(var q in crown){ q.position=new Vector3(q.position.x,LH(q.position)-0.25f*q.height,q.position.z); if(sbK.Length>0)sbK.Append(','); sbK.Append(Pl(q)); } js.Append(sbK);
    js.Append("],\"lobbyCenter\":"+V(WorldBounds.LOBBY_CENTER)+",\"spawnYaw\":"+F(LobbyLayout.SPAWN_YAW)+",\"spawn\":"+V(LobbyLayout.SPAWN)+",\"vistaGaps\":["+F(LobbyLayout.VistaGaps[0].x)+","+F(LobbyLayout.VistaGaps[1].x)+"]");
    js.Append(",\"belt\":[");
    var belt=LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT,3);
    foreach(var q in belt) q.position=new Vector3(q.position.x,LH(q.position)-0.2f,q.position.z);
    var sbT=new StringBuilder(); foreach(var q in belt){ if(sbT.Length>0)sbT.Append(','); sbT.Append(Pl(q)); } js.Append(sbT);
    js.Append("]}");
    Console.WriteLine($"fence {segs.Count}, belt {belt.Count}");
    Console.WriteLine($"lobby {lp.Count}, backdrop {bp.Count}"); File.WriteAllText(Path.Combine(outDir,"world.json"),js.ToString());
  }
}
