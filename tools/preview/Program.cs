using System; using System.IO; using System.Text; using System.Globalization; using UnityEngine; using HorseRace.EditorTools;
class P {
  static string F(float v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
  static readonly System.Collections.Generic.Dictionary<string,Color> TINTS=new System.Collections.Generic.Dictionary<string,Color>();
  static string C(Color c)=>"["+F(c.r)+","+F(c.g)+","+F(c.b)+"]";
  // 素材ごとの色の決まり（ArtPalette.StyleFor の tint、art-r2-13）を world.json の "tints" に集める
  static void Tint(string a,string p){ string k=a+"|"+(p??""); if(!TINTS.ContainsKey(k)) TINTS[k]=ArtPalette.StyleFor(a,p).tint; }
  static string Pl(Placement q){ Tint(q.asset,q.part); return "{\"a\":\""+q.asset+"\",\"p\":"+(q.part==null?"null":"\""+q.part+"\"")+",\"pos\":"+V(q.position)+",\"yaw\":"+F(q.yawDeg)+",\"roll\":"+F(q.rollDeg)+",\"s\":"+F(q.scale)+(q.scaleY!=1f||q.scaleZ!=1f?",\"sv\":"+V(new Vector3(q.scale,q.scale*q.scaleY,q.scale*q.scaleZ)):"")+"}"; }
  // Build World の見どころ・ゴール・スタートの素材（SetPieceBuilder / CourseBuilder と同じ置き方の近似。下見用）
  static string X(string a,string p,Vector3 pos,float yaw,float s,float roll=0f,Vector3? sv=null,float pitch=0f){ Tint(a,p); return "{\"a\":\""+a+"\",\"p\":"+(p==null?"null":"\""+p+"\"")+",\"pos\":"+V(pos)+",\"yaw\":"+F(yaw)+",\"roll\":"+F(roll)+(pitch!=0f?",\"pitch\":"+F(pitch):"")+",\"s\":"+F(s)+(sv.HasValue?",\"sv\":"+V(sv.Value):"")+"}"; }
  // 立てた丸太の杭（GenUtil.WoodPost / SetPieceBuilder.UprightLog と同じ：足元 g、高さ h、太さ t、art-r3-02）
  static string Post(Vector3 g,float yaw,float h,float t){ return X("Packs/WoodLogs/WoodLogs.fbx","WoodLogs_01",g+Vector3.up*(h*0.5f),yaw,1f,90f,CourseMarks.PostScale(h,t)); }
  // 手配書の紙 WantedPosters_02（Blender で測った外形：厚さ X 0.11・高さ Y 0.63・幅 Z 0.45、原点は下の辺の真ん中、面の向き = ローカル X）。
  // 迫ってくる騎手（向き faceYaw）に面を向ける：ローカル X を faceYaw へ（yaw − 90）。色は素材ごとのまま（下見）
  static string Sheet(Vector3 bottom,float faceYaw,float w,float hgt){ return X("Packs/WantedPosters/WantedPosters.fbx","WantedPosters_02",bottom,faceYaw-90f,1f,0f,new Vector3(1f,hgt/0.63f,w/0.45f)); }
  // 立て札（SetPieceBuilder.SignPost と同じ：丸太の柱 2.6m + 0.7 × (拡大 − 1)、紙 1.7 × 0.7 × 拡大を 1.55m の高さ、柱の手前 0.18m）
  static void SignP(System.Collections.Generic.List<string> l,Vector3 g,float yaw,float scale=1f){ float ph=2.6f+0.7f*(scale-1f); float a=yaw*Mathf.Deg2Rad; Vector3 f=new Vector3(Mathf.Sin(a),0f,Mathf.Cos(a));
    l.Add(X("Packs/WoodLogs/WoodLogs.fbx","WoodLogs_01",g+Vector3.up*(ph*0.5f),yaw,1f,90f,new Vector3(ph/1.5f,1.1f,1.1f))); l.Add(Sheet(g+Vector3.up*1.55f-f*0.18f,yaw,1.7f*scale,0.7f*scale)); }
  static float G(float[,] h,float x,float z)=>WorldLayout.SampleHeight(h,StageDefinitions.SIZE,HeightmapGen.HEIGHT_M,x,z);
  static Vector3 GP(float[,] h,Vector3 p)=>new Vector3(p.x,G(h,p.x,p.z),p.z);
  static string Extras(StageDef d,Vector3[] local,float[,] h0,float[,] h,float goal,SetPieces.ShortcutPlan sc){
    var l=new System.Collections.Generic.List<string>();
    const string DP="Packs/DesertProps/DesertProps.fbx",FP="Packs/FarmProps/FarmProps.fbx",HB="Packs/HayBales/HayBales.fbx",DR="Packs/DesertRocks/DesertRocks.fbx",WL="Packs/WoodLogs/WoodLogs.fbx";
    const string CL="Packs/Cliffs/Cliffs.fbx",WP="Packs/WantedPosters/WantedPosters.fbx";
    float hw0(float a)=>StageDefinitions.HalfWidthAt(d,a);
    // スタートの門（柵 9 枚）と両端の街灯・干し草
    {float a=StageDefinitions.START_ARC_M+3f; Vector3 t=WorldLayout.TangentAtArc(local,a),r=WorldLayout.RightOf(t),c=WorldLayout.PointAtArc(local,a); float yaw=WorldLayout.YawDeg(t); float half=CourseGeometry.START_GATE_PIECES*2.74f*0.5f;
     for(int i=0;i<CourseGeometry.START_GATE_PIECES;i++) l.Add(X(FP,"FarmProps_03",GP(h,c+r*(-half+(i+0.5f)*2.74f)),yaw,1f));
     for(int s=-1;s<=1;s+=2){ l.Add(X(FP,"FarmProps_02",GP(h,c+r*(s*(half+1.2f))),yaw,1f)); l.Add(X(HB,"HayBales_01",GP(h,c+r*(s*(half+3f))-t*0.8f),yaw,1f)); }}
    // ゴールゲート（art-r2-07）：CourseBuilder.GoalGate と同じ CourseMarks.GoalGateParts の数字で置く
    {Vector3 t=WorldLayout.TangentAtArc(local,goal),c=GP(h,WorldLayout.PointAtArc(local,goal));
     foreach(var gp in CourseMarks.GoalGateParts(c,t,p=>GP(h,p))) l.Add(X(gp.asset,gp.part,gp.position,gp.euler.y,1f,gp.euler.z,gp.scale));}
    // 全力の合図（街灯 2 本と札の柱）
    {float a=StageDefinitions.DashArc(d,goal); Vector3 t=WorldLayout.TangentAtArc(local,a),r=WorldLayout.RightOf(t),c=WorldLayout.PointAtArc(local,a); float yaw=WorldLayout.YawDeg(t), w=hw0(a);
     for(int s=-1;s<=1;s+=2) l.Add(Post(GP(h,c+r*(s*(w+CourseMarks.DASH_LAMP_OUT_M))),yaw,DressingPlan.LAMP_POST_H,DressingPlan.LAMP_POST_THICK));   // art-r4-14：野の灯りは丸太の柱
     l.Add(X(WL,"WoodLogs_01",GP(h,c+r*(w+CourseMarks.DASH_SIGN_OUT_M)+t*3f)+Vector3.up*1.3f,yaw,1f,90f,new Vector3(2.6f/1.5f,1.1f,1.1f)));}
    // 岩の橋（地形の背）：縁の岩・背の両脇の崖・街灯
    float br=SetPieces.BridgeArc(d,goal);
    if(br>=0f){ Vector3 t=WorldLayout.TangentAtArc(local,br),r=WorldLayout.RightOf(t),c=GP(h,WorldLayout.PointAtArc(local,br)); float yaw=WorldLayout.YawDeg(t); float lip=SetPieces.TRENCH_HALF_WIDTH+SetPieces.TRENCH_BLEND+0.8f; int gi=0;
      for(int e=-1;e<=1;e+=2) for(int s=-1;s<=1;s+=2){ int li=0; for(float la=SetPieces.SPINE_HALF+2.5f;la<SetPieces.DETOUR_FROM-2f;la+=SetPieces.LIP_ROCK_STEP,gi++,li++){ if(!SetPieces.LipRockKept(la,li)) continue; l.Add(X(DR,"DesertRocks_0"+(4+gi%6),GP(h,c+t*(e*lip)+r*(s*la))-Vector3.up*0.3f,yaw+90f+gi*37f,0.9f)); } }
      foreach(var m in SetPieces.SpineEdgeLamps()) l.Add(Post(GP(h,c+t*m.x+r*m.y),yaw,DressingPlan.LAMP_POST_H,DressingPlan.LAMP_POST_THICK));   // course-r3-04 → art-r4-14：背の縁の灯りの柱
      // art-r4-12：背の縁の平たい岩（DesertRocks_08 ×1.3、上の面が道 + 0.35m）・丸太の手すり・溝の底の骨の馬
      {float half=SetPieces.TRENCH_HALF_WIDTH+SetPieces.TRENCH_BLEND; int k2=0;
       for(int s=-1;s<=1;s+=2){ for(float a=-half;a<=half+0.01f;a+=SetPieces.SPINE_ROCK_STEP_M,k2++){ Vector3 q=c+t*a+r*(s*(SetPieces.SPINE_HALF+0.6f)); float hh=1.6f*SetPieces.SPINE_ROCK_SCALE; l.Add(X(DR,"DesertRocks_08",new Vector3(q.x,c.y+SetPieces.SPINE_ROCK_TOP_M-hh,q.z),yaw+90f+k2*47f,SetPieces.SPINE_ROCK_SCALE)); }
         Vector3 prev=Vector3.zero; for(float a=-half;a<=half+0.01f;a+=SetPieces.SPINE_RAIL_POST_STEP_M){ Vector3 gp=GP(h,c+t*a+r*(s*(SetPieces.SPINE_HALF-0.4f))); l.Add(Post(gp,yaw,1.1f,0.2f));
           if(a>-half+0.01f){ Vector3 mid=(prev+gp)*0.5f+Vector3.up*SetPieces.SPINE_RAIL_H; l.Add(X(WL,"WoodLogs_01",mid,yaw+90f,1f,0f,CourseMarks.PostScale(Vector3.Distance(prev,gp)+0.1f,0.22f))); } prev=gp; } }
       for(int i=0;i<SetPieces.TRENCH_SKELETONS;i++){ float s=i%2==0?1f:-1f; Vector3 q=GP(h,c+t*((i-1)*4f)+r*(s*(13f+i*6f))); l.Add(X("SkeletonHorse.fbx",null,q-Vector3.up*0.15f,yaw+i*70f,1f,85f)); l.Add(X(DP,"DesertProps_07",q+new Vector3(2f,0f,1f),i*50f,1.1f)); l.Add(X(DP,"DesertProps_08",q+new Vector3(-2f,0f,1.5f),i*80f,1.1f)); }
       for(int e=-1;e<=1;e+=2) for(int s=-1;s<=1;s+=2) l.Add(X("Packs/Campfire/Campfire.fbx","Campfire_01",GP(h,c+t*(e*(half+6f))+r*(s*(SetPieces.SPINE_HALF+3f))),s*40f,1f)); }
      {float wa=br-SetPieces.BRIDGE_WARN_BEFORE; Vector3 wt=WorldLayout.TangentAtArc(local,wa); SignP(l,GP(h,WorldLayout.PointAtArc(local,wa)+WorldLayout.RightOf(wt)*(hw0(wa)+CourseMarks.CORNER_BOARD_OUT_M)),WorldLayout.YawDeg(wt));}
      for(int s=-1;s<=1;s+=2) for(int e=-1;e<=1;e+=2){ int k=(s+1)+(e+1)/2; float sc0=0.32f+0.04f*k; Vector3 sp=c+r*(s*(SetPieces.SPINE_HALF+3f))+t*(e*3.5f); float mn=float.MaxValue,mx=float.MinValue;
        for(int q=0;q<5;q++){ float an=(45f+q*90f)*Mathf.Deg2Rad; Vector3 pp=q==4?sp:sp+new Vector3(Mathf.Sin(an)*3.5f,0f,Mathf.Cos(an)*3.5f); float gy=G(h,pp.x,pp.z); mn=Mathf.Min(mn,gy); mx=Mathf.Max(mx,gy); }
        l.Add(X(DR,"DesertRocks_0"+(1+k%3),new Vector3(sp.x,mn-Mathf.Min(mx-mn,4f)-0.1f,sp.z),yaw+(s>0?0f:180f)+k*23f,sc0,0f,new Vector3(sc0,sc0*1.5f,sc0))); } }   // art-r4-12：溝の口の縁へ下ろす
    // 踏み切り（地形）：杭の列・干し草・骨
    float jp=SetPieces.JumpArc(d,goal);
    if(jp>=0f){ Vector3 t=WorldLayout.TangentAtArc(local,jp),r=WorldLayout.RightOf(t),lp=GP(h,WorldLayout.PointAtArc(local,jp)); float yaw=WorldLayout.YawDeg(t);
      for(int s=-1;s<=1;s+=2){ for(float a=-SetPieces.JUMP_LENGTH;a<=0f;a+=SetPieces.JUMP_LAMP_STEP_M) l.Add(Post(GP(h,lp+t*a+r*(s*(SetPieces.KICKER_HALF_WIDTH+1.5f))),yaw,DressingPlan.LAMP_POST_H,DressingPlan.LAMP_POST_THICK)); Vector3 hp=GP(h,lp+r*(s*9f)-t*0.5f); l.Add(X(HB,"HayBales_01",hp,yaw,1f)); l.Add(X(HB,"HayBales_01",hp+Vector3.up*1.55f,yaw+40f,0.9f));
        SignP(l,GP(h,lp+r*(s*(hw0(jp)+1.5f))-t*1f),yaw,1.3f); }   // course-r3-13：青緑の街灯の対・縁の「跳べ ↑」
      {float wa=jp-SetPieces.JUMP_LENGTH-SetPieces.JUMP_SIGN_BEFORE_M; Vector3 wt=WorldLayout.TangentAtArc(local,wa); SignP(l,GP(h,WorldLayout.PointAtArc(local,wa)+WorldLayout.RightOf(wt)*(hw0(wa)+CourseMarks.CORNER_BOARD_OUT_M)),WorldLayout.YawDeg(wt),1.2f);}
      l.Add(X("SkeletonHorse.fbx",null,GP(h,lp+t*22f+r*18f)-Vector3.up*0.15f,yaw+30f,1f,85f));
      for(int i=0;i<3;i++) l.Add(X(DP,i==0?"DesertProps_07":"DesertProps_08",GP(h,lp+t*(16f+i*6f)-r*(16f+i)),yaw+i*70f,1.1f)); }
    // 近道の入口の街灯と札の柱
    if(sc!=null){ Vector3 dir=new Vector3(sc.b.x-sc.a.x,0f,sc.b.z-sc.a.z).normalized,r=WorldLayout.RightOf(dir); Vector3 e0=Vector3.Lerp(sc.a,sc.b,sc.f0); float yaw=WorldLayout.YawDeg(dir);
      for(int s=-1;s<=1;s+=2) l.Add(Post(GP(h,e0+r*(s*(SetPieces.SHORTCUT_HALF_WIDTH+1.5f))),yaw,DressingPlan.LAMP_POST_H,DressingPlan.LAMP_POST_THICK));
      l.Add(X(WL,"WoodLogs_01",GP(h,e0+r*(SetPieces.SHORTCUT_HALF_WIDTH+3.5f)-dir*2f)+Vector3.up*1.3f,yaw,1f,90f,new Vector3(2.6f/1.5f,1.1f,1.1f))); }
    // 急カーブの札（course-r2-08）・区間の門の柱（course-r2-15）：丸太の柱（立てた WoodLogs_01）
    // course-r3-07/11：札の紙も書き出す（70・35m は柵の線の上に 1.8 倍）、頂点の干し草と街灯、外側の干し草の壁（2 段）
    foreach(var cb in CourseMarks.CornerBoards(d,local,goal)) SignP(l,GP(h,cb.position),cb.yaw,cb.scale);
    foreach(var cn in StageDefinitions.TightCorners(d,local)){ var m=CourseMarks.CornerApexMarks(d,local,cn,goal); float cy=WorldLayout.YawDeg(WorldLayout.TangentAtArc(local,cn.apexArc));
      l.Add(Post(GP(h,m[1]),cy,DressingPlan.LAMP_POST_H,DressingPlan.LAMP_POST_THICK));   // art-r4-11/14：頂点の内側の干し草はやめ、灯りは丸太の柱
      foreach(var wb in CourseMarks.CornerWall(d,local,cn)){ Vector3 hp=GP(h,wb.position); l.Add(X(HB,wb.part,hp-Vector3.up*0.08f,wb.yaw,wb.scale)); if(wb.cap) l.Add(X(HB,"HayBales_01",hp+Vector3.up*(CourseMarks.HAY_BLOCK_H*wb.scale-0.12f),wb.yaw+37f,CourseMarks.CORNER_WALL_CAP_SCALE)); }
      foreach(var ch in CourseMarks.CornerWallChevrons(d,local,cn)){ Vector3 top=GP(h,ch.position)+Vector3.up*(ch.position.y-0.1f); l.Add(Post(top,ch.yaw,1.3f,0.2f)); float a2=ch.yaw*Mathf.Deg2Rad; Vector3 f2=new Vector3(Mathf.Sin(a2),0f,Mathf.Cos(a2)); l.Add(Sheet(top+Vector3.up*0.55f-f2*0.18f,ch.yaw,1.2f,0.6f)); } }
    // course-r3-01/11：区間の門は柱・細い横木（CourseMarks.SectorBeamPieces、CourseBuilder と同じ）・残りの距離の紙
    {var arcs=CourseMarks.SectorGateArcs(goal); var posts=CourseMarks.SectorGatePosts(d,local,goal);
     for(int k=0;k<arcs.Count;k++){ Vector3 t=WorldLayout.TangentAtArc(local,arcs[k]); t=new Vector3(t.x,0f,t.z).normalized; Vector3 rt=WorldLayout.RightOf(t); float yaw=WorldLayout.YawDeg(t);
       Vector3 lg=GP(h,posts[2*k]), rg=GP(h,posts[2*k+1]); const float H=CourseMarks.SECTOR_POST_H;
       l.Add(X(WL,"WoodLogs_01",lg+Vector3.up*(H*0.5f),yaw,1f,90f,new Vector3(H/1.5f,1.1f,1.1f))); l.Add(X(WL,"WoodLogs_01",rg+Vector3.up*(H*0.5f),yaw,1f,90f,new Vector3(H/1.5f,1.1f,1.1f)));
       float top=Mathf.Max(lg.y,rg.y)+H-0.4f;
       foreach(var bp in CourseMarks.SectorBeamPieces(lg-rt*0.55f,rg+rt*0.55f,top)) l.Add(X(bp.asset,bp.part,bp.position,bp.euler.y,1f,0f,bp.scale));
       Vector3 mid=(lg+rg)*0.5f; mid.y=top-1.9f-0.75f; l.Add(Sheet(mid-t*0.05f,yaw,4.2f,1.5f));
       for(int sd=-1;sd<=1;sd+=2) l.Add(Post(GP(h,(sd<0?lg:rg)+rt*(sd*1.6f)-t*0.8f),yaw,DressingPlan.LAMP_POST_H,DressingPlan.LAMP_POST_THICK)); }}
    // course-r3-08：ゴール前の見物の直線（CourseMarks.FinishChute、CourseBuilder と同じ）
    foreach(var cp in CourseMarks.FinishChute(d,local,goal)){ Vector3 g=GP(h,cp.position);
      if(cp.kind==CourseMarks.CHUTE_SIGN){ SignP(l,g,cp.yaw,1.4f); continue; }
      if(cp.kind==CourseMarks.CHUTE_LAMP&&d.kind!=HeightmapGen.KIND_MESA){ l.Add(Post(g,cp.yaw,DressingPlan.LAMP_POST_H,DressingPlan.LAMP_POST_THICK)); continue; }   // art-r4-14
      if(cp.kind==CourseMarks.CHUTE_FLAG){ float H=CourseMarks.CHUTE_POST_H; l.Add(Post(g,cp.yaw,H,CourseMarks.CHUTE_POST_THICK));
        float a=cp.yaw*Mathf.Deg2Rad; Vector3 rr=new Vector3(Mathf.Cos(a),0f,-Mathf.Sin(a)); l.Add(Sheet(g+Vector3.up*(H-1.25f)-rr*(cp.side*0.55f),cp.yaw,0.9f,1.1f)); continue; }
      l.Add(X(cp.asset,cp.part,g-Vector3.up*0.03f,cp.yaw,cp.scale.x,0f,(cp.scale.x==1f&&cp.scale.y==1f&&cp.scale.z==1f)?(Vector3?)null:cp.scale)); }
    return string.Join(",",l);
  }
  // ロビーの本物の置き物（art-r3-12）：LobbyBuilder（Plaza・Corral・Hangout・Practice・TownClock・PosterWall・Stable）と同じ位置・素材の近似。
  // ロビー中心からの相対、通りの高さ y = 0。前の下見は色の円柱・箱だった
  static string LobbyProps(System.Collections.Generic.List<Vector3[]> fence){
    var l=new System.Collections.Generic.List<string>();
    const string FP="Packs/FarmProps/FarmProps.fbx",HB="Packs/HayBales/HayBales.fbx",WL="Packs/WoodLogs/WoodLogs.fbx",NB="Props/NoticeBoard/NoticeBoard.fbx",CF="Packs/Campfire/Campfire.fbx";
    Vector3 up=Vector3.up;
    // 柵（LobbyLayout.FenceSegments、FarmProps_03 を高さ ×1.6）
    foreach(var sg in fence){ Vector3 dd=sg[1]-sg[0]; float len=new Vector3(dd.x,0f,dd.z).magnitude; if(len<0.1f) continue;
      l.Add(X(FP,"FarmProps_03",(sg[0]+sg[1])*0.5f,WorldLayout.YawDeg(new Vector3(dd.x,0f,dd.z).normalized)+90f,1f,0f,new Vector3(len/2.74f,LobbyLayout.FenceScaleYAt((sg[0]+sg[1])*0.5f),1f))); }   // art-r4-09：切れ目の柵は低い
    // 広場：受付の机・樽・木箱・街灯・札の板、操作の紙の杭、手配書の壁
    Vector3 desk=LobbyLayout.LEDGER_DESK;
    l.Add(X(FP,"FarmProps_05",desk,180f,1f)); l.Add(X(FP,"FarmProps_15",desk+new Vector3(2.4f,0f,1.2f),0f,1f)); l.Add(X(FP,"FarmProps_12",desk+new Vector3(1.2f,0f,2.2f),20f,1f));
    l.Add(X(FP,"FarmProps_02",desk+new Vector3(0f,0f,1.8f),180f,1f)); l.Add(X(NB,null,desk+new Vector3(0f,0f,1.0f),0f,0.5f)); l.Add(Post(desk+new Vector3(2.6f,0f,-0.4f),0f,1.56f,0.16f));
    l.Add(X(NB,null,LobbyLayout.WANTED_WALL,0f,1.2f));
    // スタート台：丸太の束 2 枚・四隅の干し草・机、札の杭
    Vector3 sb=LobbyLayout.START_BELL;
    for(int i=0;i<2;i++) l.Add(X(WL,"WoodLogs_01",sb+new Vector3(0f,0f,-0.9f+i*1.8f),90f*i,2f));
    for(int i=0;i<4;i++) l.Add(X(HB,"HayBales_01",sb+new Vector3((i%2==0?-1f:1f)*2.3f,0f,(i<2?-1f:1f)*2.3f),i*47f,0.7f));
    l.Add(X(FP,"FarmProps_05",sb+up*0.6f,180f,0.45f)); l.Add(Post(sb+new Vector3(0f,0.6f,-0.85f),0f,1.31f,0.16f));
    l.Add(X(FP,"FarmProps_02",LobbyLayout.HUD_LEVER,180f,0.6f));
    // 町の大時計：掲示板 ×1.4 を 0.8m 持ち上げ、脚の杭 2 本と街灯 2 本
    Vector3 tc=LobbyLayout.TOWN_CLOCK; float hw=4.5f*LobbyLayout.TOWN_CLOCK_SCALE*0.5f;
    l.Add(X(NB,null,tc+up*0.8f,0f,LobbyLayout.TOWN_CLOCK_SCALE));
    for(int i=0;i<2;i++){ Vector3 sp=tc+new Vector3(i==0?-hw+0.35f:hw-0.35f,0f,0f); l.Add(Post(sp,0f,1.72f,0.16f)); l.Add(X(FP,"FarmProps_02",sp+new Vector3(i==0?-0.9f:0.9f,0f,-0.4f),180f,1f)); }
    // 柵の馬 7 頭（6 頭目は骨の馬）とつなぐ杭、柵
    Vector3 cf=LobbyLayout.CORRAL_FIRST; float cs=LobbyLayout.CORRAL_STEP;
    { Vector3 a=cf+new Vector3(-3.5f,0f,-1.5f), b=cf+new Vector3(-3.5f,0f,6f*cs+0.8f); float len=(b-a).magnitude; int n=Mathf.CeilToInt(len/2.74f);
      for(int i=0;i<n;i++) l.Add(X(FP,"FarmProps_03",Vector3.Lerp(a,b,(i+0.5f)/n),90f,1f,0f,new Vector3(len/n/2.74f,1f,1f))); }
    for(int i=0;i<7;i++){ Vector3 p=cf+new Vector3(0f,0f,i*cs); l.Add(X(i==6?"SkeletonHorse.fbx":"Horse.fbx",null,p,90f,1f)); l.Add(Post(p+new Vector3(-2.6f,0f,0f),90f,1.4f,0.16f)); }
    l.Add(X("Props/Stable/Stable.fbx",null,LobbyLayout.STABLE,180f,1f));
    // たまり場：焚き火・丸太のベンチ 4 本・薪の山・掲示板 3 枚・前回レースの黒板
    Vector3 hf=LobbyLayout.HANGOUT_FIRE;
    l.Add(X(CF,"Campfire_01",hf,0f,1f));
    for(int i=0;i<4;i++){ float a=i*90f+45f; Vector3 p=hf+new Vector3(Mathf.Sin(a*Mathf.Deg2Rad),0f,Mathf.Cos(a*Mathf.Deg2Rad))*3.2f; l.Add(X(WL,"WoodLogs_01",p,a+90f,1.4f)); }
    for(int i=0;i<6;i++) l.Add(X(WL,"WoodLogs_0"+(2+i),hf+new Vector3(-7f+(i%3)*0.9f,(i/3)*0.3f,-5f),90f+i*7f,1.2f));
    for(int i=-1;i<=1;i++) l.Add(X(NB,null,LobbyLayout.NOTICE_BOARDS+new Vector3(i*6f,0f,0f),180f,1f));
    l.Add(X(NB,null,LobbyLayout.LAST_RACE_BOARD,-90f,0.6f));
    // 練習場：ジャンプ台の丸太の束（13° 上り）、丘の縁の丸太、干し草、カカシ、杭と練習用の馬、看板、遺物
    Vector3 jp=LobbyLayout.PRACTICE_JUMP; float ang=SetPieces.JumpAngleDeg;
    for(int i=0;i<3;i++) for(int j=0;j<2;j++){ float lz=(i-1)*2f; l.Add(X(WL,"WoodLogs_01",jp+new Vector3((j-0.5f)*3f,0.25f+lz*Mathf.Tan(ang*Mathf.Deg2Rad),lz),0f,2f,0f,null,ang)); }
    for(int i=0;i<6;i++){ float a=i*60f; l.Add(X(WL,"WoodLogs_0"+(2+i%3),LobbyLayout.PRACTICE_BUMP+new Vector3(Mathf.Sin(a*Mathf.Deg2Rad),0f,Mathf.Cos(a*Mathf.Deg2Rad))*5.2f,a+90f,1.4f)); }
    float[] hayZ={91f,97f,103f,114f,120f,126f,135f};
    for(int i=0;i<hayZ.Length+3;i++){ Vector3 rel=i<hayZ.Length?new Vector3(34.8f,0f,hayZ[i]):new Vector3(21f+(i-hayZ.Length)*6f,0f,138.5f); l.Add(X(HB,i%3==0?"HayBales_01":"HayBales_0"+(2+i%5),rel,i<hayZ.Length?90f:0f,1f)); }
    foreach(var dm in new[]{LobbyLayout.PRACTICE_DUMMY_1,LobbyLayout.PRACTICE_DUMMY_2}){ l.Add(X(HB,"HayBales_01",dm,180f,0.7f)); l.Add(X(HB,"HayBales_01",dm+up*1.1f,220f,0.6f)); }
    for(int i=0;i<2;i++){ Vector3 pp=LobbyLayout.PRACTICE_POST_FIRST+new Vector3(0f,0f,i*4.5f); l.Add(Post(pp,90f,1.4f,0.16f)); l.Add(X("Horse.fbx",null,pp+new Vector3(2.5f,0f,0f),0f,1f)); }
    l.Add(X(NB,null,LobbyLayout.PRACTICE_POST_FIRST+new Vector3(1.5f,0f,-2.5f),0f,0.6f)); l.Add(X(NB,null,LobbyLayout.PRACTICE_CONTROLS,0f,0.8f));
    l.Add(X("Packs/Gems/Gems.fbx","Gems_04_01",LobbyLayout.PRACTICE_RELIC+up*0.25f,0f,1.1f/1.208f));
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
      var sand=HeightmapGen.SandMask(StageDefinitions.ALPHAMAP_RES,d.kind,d.seed,d.kind!=HeightmapGen.KIND_DUNES?h:null);   // 境目はくぼ地（art-r3-07）
      int n=h.GetLength(0);
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"h{d.index}.bin")))){ bw.Write(n); for(int z=0;z<n;z++)for(int x=0;x<n;x++)bw.Write(h[z,x]); }
      // 道の踏み固めた土（art-r2-02）。art-r4-05：TerrainBuilder と同じく道の上の砂を消し（砂漠は縁の外を砂のまま）、深い砂を足し、轍の距離（G）も書き出す
      var trail=HeightmapGen.TrailMask(StageDefinitions.ALPHAMAP_RES,d,norm,out float[,] tdist,out float[,] thw);
      HeightmapGen.ClearSandOnTrail(sand,trail,d.kind,tdist,thw);
      HeightmapGen.ApplyDeepSand(sand,trail,d,local,StageDefinitions.SIZE);
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"t{d.index}.bin")))){ int trn=trail.GetLength(0); bw.Write(trn); for(int z=0;z<trn;z++)for(int x=0;x<trn;x++)bw.Write(trail[z,x]); }
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"g{d.index}.bin")))){ int trn=tdist.GetLength(0); bw.Write(trn); for(int z=0;z<trn;z++)for(int x=0;x<trn;x++)bw.Write(Mathf.Clamp01(tdist[z,x]/HeightmapGen.TRAIL_DIST_MAX_M)); }
      int m=sand.GetLength(0);
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"s{d.index}.bin")))){ bw.Write(m); for(int z=0;z<m;z++)for(int x=0;x<m;x++)bw.Write(sand[z,x]); }
      var ch=SetPieces.WithHeights(local,h);
      var sbC=new StringBuilder(); for(int i=0;i<ch.Length;i+=5){ if(sbC.Length>0)sbC.Append(','); sbC.Append(V(ch[i])); }
      var cps=WorldLayout.CheckpointArcs(goal,StageDefinitions.CP_SPACING_M);
      var sbP=new StringBuilder(); foreach(var c in cps){ if(sbP.Length>0)sbP.Append(','); var p=WorldLayout.PointAtArc(ch,c); sbP.Append(V(p)); }
      var lines=HeightmapGen.LineMask(StageDefinitions.ALPHAMAP_RES,d,local,goal);   // course-r3-14：スタート・ゴールの線
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"l{d.index}.bin")))){ int ln=lines.GetLength(0); bw.Write(ln); for(int z=0;z<ln;z++)for(int x=0;x<ln;x++)bw.Write(lines[z,x]); }
      // course-r3-11：ゴールの弧長と近道の入口の、書き出したコースの点（10m ごと）の添字
      int goalK=Mathf.RoundToInt(goal/StageDefinitions.SAMPLE_SPACING_M/5f), scEntryK=-1;
      if(plan!=null){ Vector3 e0=Vector3.Lerp(plan.a,plan.b,plan.f0); float bd=float.MaxValue; for(int i=0;i<ch.Length;i+=5){ float dd=WorldLayout.DistXZ(ch[i],e0); if(dd<bd){ bd=dd; scEntryK=i/5; } } }
      var sbR=new StringBuilder(); var relicGems=new System.Collections.Generic.List<string>();
      foreach(var rs in SetPieces.RelicSpots(d,local,goal,plan)){ if(sbR.Length>0)sbR.Append(','); Vector3 rp=GP(h,rs.local)+Vector3.up*rs.up; sbR.Append(V(rp));
        // art-r3-04：遺物は本物の宝石（CourseBuilder.Relic と同じ 1.1m）と足元の小石の輪
        string gp="Gems_0"+(d.index%4+1)+"_01"; relicGems.Add(X("Packs/Gems/Gems.fbx",gp,rp+Vector3.up*(0.8f-0.55f),0f,1.1f/1.208f));
        for(int pi=0;pi<5;pi++){ float pa=(pi*72f+18f)*Mathf.Deg2Rad; relicGems.Add(X("Packs/RockCollection/RockCollection.fbx","RockCollection_"+(8*(1+((d.index*5+pi)%23))).ToString("00"),GP(h,rs.local)+new Vector3(Mathf.Sin(pa)*2f,-0.05f,Mathf.Cos(pa)*2f),pi*71f,2.2f)); } }
      float br=SetPieces.BridgeArc(d,goal), jp=SetPieces.JumpArc(d,goal);
      string bridge= br<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,br))+",\"t\":"+V(WorldLayout.TangentAtArc(local,br))+"}";
      string jump= jp<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,jp))+",\"t\":"+V(WorldLayout.TangentAtArc(local,jp))+"}";
      string sc= plan==null? "null" : "{\"a\":"+V(plan.a)+",\"b\":"+V(plan.b)+",\"drop\":"+F(plan.drop)+"}";
      var dp=DressingPlan.For(d,local,h,goal,plan);
      dp.AddRange(DressingPlan.BarrierLine(d,local,h,goal,CourseBarrier.For(d,local,goal,plan)));   // 柵の線の目印（course-r2-06）
      var sbD=new StringBuilder(); foreach(var q in dp){ if(sbD.Length>0)sbD.Append(','); sbD.Append(Pl(q)); }
      if(d.index>0) js.Append(',');
      float ps=SetPieces.PassArc(d,goal);
      string pass= ps<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,ps))+",\"t\":"+V(WorldLayout.TangentAtArc(local,ps))+"}";
      string lm="null"; if(DressingPlan.LandmarkSpot(d,local,goal,h,out Vector3 lmp,out float lma)) lm=V(GP(h,lmp));
      var li=LightingPlan.ForStage(d.index);
      js.Append("{\"index\":"+d.index+",\"trail\":"+C(ArtPalette.Trail(d.index))+",\"dirt\":"+C(ArtPalette.Dirt(d.index))+",\"sand\":"+C(ArtPalette.Sand(d.index))+",\"strata\":["+C(ArtPalette.StrataLightFor(d.index))+","+C(ArtPalette.StrataDarkFor(d.index))+"]"+",\"sky\":"+C(LightingPlan.SkyTint(d.index))+",\"rutShade\":"+F(ArtPalette.RUT_SHADE)+",\"rutOffset\":"+F(HeightmapGen.RUT_OFFSET_M)+",\"rutWidth\":"+F(HeightmapGen.RUT_WIDTH_M)+",\"trailDistMax\":"+F(HeightmapGen.TRAIL_DIST_MAX_M)+",\"fog\":"+C(li.fogColor)+",\"sun\":"+V(li.sunEuler)+",\"sunColor\":"+C(li.sunColor)+",\"label\":\""+d.label+"\",\"center\":"+V(d.center)+",\"goal\":"+F(goal)+",\"goalK\":"+goalK+",\"scEntryK\":"+scEntryK+",\"course\":["+sbC+"],\"cps\":["+sbP+"],\"relics\":["+sbR+"],\"bridge\":"+bridge+",\"jump\":"+jump+",\"pass\":"+pass+",\"landmark\":"+lm+",\"shortcut\":"+sc+",\"dress\":["+sbD+"],\"extras\":["+Extras(d,local,h0,h,goal,plan)+"]}");
      Console.WriteLine($"stage {d.index} {d.label} goal {goal:0} m, cps {cps.Length}, dress {dp.Count}");
    }
    js.Append("],\"lobby\":[");
    var tn=LobbyLayout.TOWN_NOMINAL_RECT;
    var lp=DressingPlan.ForLobby(new Vector2(tn.x,tn.y),new Vector2(tn.z,tn.w),LobbyLayout.LobbyDressingExclusions(),4242);
    var sbL=new StringBuilder(); foreach(var q in lp){ if(sbL.Length>0)sbL.Append(','); sbL.Append(Pl(q)); } js.Append(sbL);
    js.Append("],\"backdrop\":[");
    var bp=DressingPlan.Backdrop(7);
    // art-r4-01：継ぎ目の輪の岩は縁をまたぐ：足元 5 点のステージの地面・外の地面（基本の高さ）の高い方のいちばん低い所 − 高さの 35%
    {var rectsB=WorldBounds.PlayableRects();
     float WG(Vector3 q){ float y=WorldBounds.FILLER_Y+WorldBounds.FillerBaseHeight(q.x,q.z); for(int i=0;i<3;i++){ var o=StageDefinitions.All[i].Origin; if(q.x>=o.x&&q.z>=o.z&&q.x<=o.x+StageDefinitions.SIZE&&q.z<=o.z+StageDefinitions.SIZE) y=Mathf.Max(y,o.y+G(HS[i],q.x-o.x,q.z-o.z)); else { float dd=WorldBounds.DistToRect(q,rectsB[i]); if(dd<WorldBounds.FILLER_BLEND_M){ Vector3 e=new Vector3(Mathf.Clamp(q.x,o.x+0.5f,o.x+StageDefinitions.SIZE-0.5f),0f,Mathf.Clamp(q.z,o.z+0.5f,o.z+StageDefinitions.SIZE-0.5f)); float eh=o.y+G(HS[i],e.x-o.x,e.z-o.z)-WorldBounds.FILLER_SEAM_M; float u=Mathf.Clamp01(dd/WorldBounds.FILLER_BLEND_M); y=Mathf.Lerp(eh,y,u*u*(3f-2f*u)); } } } return y; }
     foreach(var q in bp){ if(q.group!="BackdropNear") continue; float foot=q.radius*0.6f, low=float.MaxValue; for(int i=0;i<5;i++){ float an=(45f+i*90f)*Mathf.Deg2Rad; Vector3 pp=i==4?q.position:q.position+new Vector3(Mathf.Sin(an)*foot,0f,Mathf.Cos(an)*foot); low=Mathf.Min(low,WG(pp)); }
       q.position=new Vector3(q.position.x,low-DressingPlan.SEAM_SINK*q.height,q.position.z); }}
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
    // 外の地面（TerrainBuilder.BuildFiller と同じ WorldBounds.FillerGrid、art-r2-01）
    var FR=WorldBounds.FillerRect(); const int FN=257;
    {var rects=WorldBounds.PlayableRects(); var lc0=WorldBounds.LOBBY_CENTER;
     var fg=WorldBounds.FillerGrid(FN,rects,(i,x,z)=> i<3 ? StageDefinitions.All[i].Origin.y+G(HS[i],x-StageDefinitions.All[i].Origin.x,z-StageDefinitions.All[i].Origin.z) : lc0.y+LH(new Vector3(x-lc0.x,0f,z-lc0.z)));
     using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,"filler.bin")))){ bw.Write(FN); bw.Write(FR.x); bw.Write(FR.y); bw.Write(FR.z); bw.Write(FR.w);
       for(int z=0;z<FN;z++)for(int x=0;x<FN;x++) bw.Write(fg[z,x]); }}
    js.Append("],\"crown\":[");
    var crown=LobbyLayout.RimCrown(LobbyLayout.FENCE_RECT,11);
    LobbyLayout.GroundCrown(crown,LH);   // art-r3-13：足元の 4 隅の一番低い地面へ（LobbyBuilder と同じ）
    var sbK=new StringBuilder(); foreach(var q in crown){ if(sbK.Length>0)sbK.Append(','); sbK.Append(Pl(q)); } js.Append(sbK);
    js.Append("],\"lobbyCenter\":"+V(WorldBounds.LOBBY_CENTER)+",\"spawnYaw\":"+F(LobbyLayout.SPAWN_YAW)+",\"spawn\":"+V(LobbyLayout.SPAWN)+",\"vistaGaps\":["+F(LobbyLayout.VistaGaps[0].x)+","+F(LobbyLayout.VistaGaps[1].x)+"]");
    js.Append(",\"belt\":[");
    var belt=LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT,3);
    foreach(var q in belt) q.position=new Vector3(q.position.x,LH(q.position)-0.2f,q.position.z);
    var sbT=new StringBuilder(); foreach(var q in belt){ if(sbT.Length>0)sbT.Append(','); sbT.Append(Pl(q)); } js.Append(sbT);
    js.Append("],\"vista\":[");
    var vis=LobbyLayout.VistaHeroes(5);
    var sbV=new StringBuilder(); foreach(var q in vis){ if(sbV.Length>0)sbV.Append(','); sbV.Append(Pl(q)); } js.Append(sbV);
    var lob=LightingPlan.ForStage(LightingPlan.LOBBY_LIGHT);
    js.Append("],\"lobbyProps\":["+LobbyProps(segs)+"]");
    js.Append(",\"fillerDirt\":"+C(ArtPalette.DirtOuter)+",\"lobbyFog\":"+C(lob.fogColor)+",\"townGround\":"+C(ArtPalette.TownGroundTint)+",\"warmRock\":"+C(ArtPalette.RockGrayTint)+",\"wood\":"+C(ArtPalette.WeatheredWood)+",\"sage\":"+C(ArtPalette.Sage)+",\"buildingTex\":"+C(ArtPalette.BuildingTextureTint));
    js.Append(",\"tints\":{");
    bool first=true; foreach(var kv in TINTS){ if(kv.Value==Color.white) continue; if(!first) js.Append(','); first=false; js.Append("\""+kv.Key+"\":"+C(kv.Value)); }
    js.Append("}}");
    Console.WriteLine($"fence {segs.Count}, belt {belt.Count}");
    Console.WriteLine($"lobby {lp.Count}, backdrop {bp.Count}"); File.WriteAllText(Path.Combine(outDir,"world.json"),js.ToString());
  }
}
