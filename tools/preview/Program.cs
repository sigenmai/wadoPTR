using System; using System.IO; using System.Text; using System.Globalization; using UnityEngine; using HorseRace.EditorTools;
class P {
  static string F(float v)=>v.ToString("0.###",CultureInfo.InvariantCulture);
  static string V(Vector3 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
  static string Pl(Placement q)=>"{\"a\":\""+q.asset+"\",\"p\":"+(q.part==null?"null":"\""+q.part+"\"")+",\"pos\":"+V(q.position)+",\"yaw\":"+F(q.yawDeg)+",\"roll\":"+F(q.rollDeg)+",\"s\":"+F(q.scale)+"}";
  static void Main(string[] a){
    string outDir=a[0]; var js=new StringBuilder("{\"stages\":[");
    foreach(var d in StageDefinitions.All){
      var local=StageDefinitions.SampleLocal(d); float goal=StageDefinitions.GoalArc(d,local);
      var norm=StageDefinitions.Normalize(local); var h=HeightmapGen.Generate(StageDefinitions.HEIGHTMAP_RES,d.kind,d.seed,norm);
      HeightmapGen.ApplyRim(h,StageDefinitions.SIZE,HeightmapGen.HEIGHT_M,HeightmapGen.RIM_WIDTH_M,HeightmapGen.RIM_MIN_M,HeightmapGen.RIM_MAX_M,d.seed,norm);
      var plan=SetPieces.ApplyTerrain(d,local,h,goal);
      var sand=HeightmapGen.SandMask(StageDefinitions.ALPHAMAP_RES,d.kind,d.seed,d.kind==HeightmapGen.KIND_CANYON?h:null);
      int n=h.GetLength(0);
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"h{d.index}.bin")))){ bw.Write(n); for(int z=0;z<n;z++)for(int x=0;x<n;x++)bw.Write(h[z,x]); }
      int m=sand.GetLength(0);
      using(var bw=new BinaryWriter(File.Create(Path.Combine(outDir,$"s{d.index}.bin")))){ bw.Write(m); for(int z=0;z<m;z++)for(int x=0;x<m;x++)bw.Write(sand[z,x]); }
      var ch=SetPieces.WithHeights(local,h);
      var sbC=new StringBuilder(); for(int i=0;i<ch.Length;i+=5){ if(sbC.Length>0)sbC.Append(','); sbC.Append(V(ch[i])); }
      var cps=WorldLayout.CheckpointArcs(goal,StageDefinitions.CP_SPACING_M);
      var sbP=new StringBuilder(); foreach(var c in cps){ if(sbP.Length>0)sbP.Append(','); var p=WorldLayout.PointAtArc(ch,c); sbP.Append(V(p)); }
      var sbR=new StringBuilder(); for(int i=0;i<d.relicFractions.Length;i++){ if(sbR.Length>0)sbR.Append(','); var p=WorldLayout.PointAtArc(ch,d.relicFractions[i]*goal); sbR.Append(V(p)); }
      float br=SetPieces.BridgeArc(d,goal), jp=SetPieces.JumpArc(d,goal);
      string bridge= br<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,br))+",\"t\":"+V(WorldLayout.TangentAtArc(local,br))+"}";
      string jump= jp<0? "null" : "{\"p\":"+V(WorldLayout.PointAtArc(ch,jp))+",\"t\":"+V(WorldLayout.TangentAtArc(local,jp))+"}";
      string sc= plan==null? "null" : "{\"a\":"+V(plan.a)+",\"b\":"+V(plan.b)+",\"drop\":"+F(plan.drop)+"}";
      var dp=DressingPlan.For(d,local,h,goal,plan);
      var sbD=new StringBuilder(); foreach(var q in dp){ if(sbD.Length>0)sbD.Append(','); sbD.Append(Pl(q)); }
      if(d.index>0) js.Append(',');
      js.Append("{\"index\":"+d.index+",\"label\":\""+d.label+"\",\"center\":"+V(d.center)+",\"goal\":"+F(goal)+",\"course\":["+sbC+"],\"cps\":["+sbP+"],\"relics\":["+sbR+"],\"bridge\":"+bridge+",\"jump\":"+jump+",\"shortcut\":"+sc+",\"dress\":["+sbD+"]}");
      Console.WriteLine($"stage {d.index} {d.label} goal {goal:0} m, cps {cps.Length}, dress {dp.Count}");
    }
    js.Append("],\"lobby\":[");
    var lp=DressingPlan.ForLobby(new Vector2(-41f,-82f),new Vector2(41f,82f),new System.Collections.Generic.List<Vector4>(),4242);
    var sbL=new StringBuilder(); foreach(var q in lp){ if(sbL.Length>0)sbL.Append(','); sbL.Append(Pl(q)); } js.Append(sbL);
    js.Append("],\"backdrop\":[");
    var bp=DressingPlan.Backdrop(7);
    var sbB=new StringBuilder(); foreach(var q in bp){ if(sbB.Length>0)sbB.Append(','); sbB.Append(Pl(q)); } js.Append(sbB);
    js.Append("],\"fence\":[");
    var segs=LobbyLayout.FenceSegments(LobbyLayout.FENCE_RECT,LobbyLayout.Gates,LobbyLayout.FENCE_PIECE_M);
    var sbF=new StringBuilder(); foreach(var sg in segs){ if(sbF.Length>0)sbF.Append(','); sbF.Append("["+V(sg[0])+","+V(sg[1])+"]"); } js.Append(sbF);
    js.Append("],\"belt\":[");
    var belt=LobbyLayout.TreeBelt(LobbyLayout.FENCE_RECT,3);
    var sbT=new StringBuilder(); foreach(var q in belt){ if(sbT.Length>0)sbT.Append(','); sbT.Append(Pl(q)); } js.Append(sbT);
    js.Append("]}");
    Console.WriteLine($"fence {segs.Count}, belt {belt.Count}");
    Console.WriteLine($"lobby {lp.Count}, backdrop {bp.Count}"); File.WriteAllText(Path.Combine(outDir,"world.json"),js.ToString());
  }
}
