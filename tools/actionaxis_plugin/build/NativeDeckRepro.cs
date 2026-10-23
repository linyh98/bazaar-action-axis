using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;
public static class NativeDeckRepro {
 public static void Main() {
 JsonValue ev=Json.Parse(File.ReadAllText("captures/diagnostics/current_combat_evidence.json"));
 JsonValue native=Json.Parse(File.ReadAllText("captures/diagnostics/native_item_rules_evidence.json"))["cards"];
 List<LiveCardEntry> entries=new List<LiveCardEntry>();int i=0;int[] slots={0,1,2,4,5,6,8,9};
 foreach(JsonValue raw in ev["playerCards"].AsArray()) {
  LiveCardEntry e=new LiveCardEntry();e.TemplateId=raw["id"].AsString();e.Tier=i<4?"Silver":"Bronze";e.Size=i==2||i==5?"Medium":"Small";e.Socket=slots[i];
  foreach(KeyValuePair<string,JsonValue> p in raw["liveAttributes"].AsObject())e.Attributes[p.Key]=(int)p.Value.AsDouble();
  if(i==1){e.Enchantment="Icy";e.EnchantmentDefinition=native[e.TemplateId]["Icy"];}if(i==4){e.Enchantment="Toxic";e.EnchantmentDefinition=native[e.TemplateId]["Toxic"];}
  entries.Add(e);i++;
 }
 CardCatalog catalog=CardCatalog.Load("src/data/cards.json");
 MappedBoard m=CardMapper.Build(entries,catalog,1175,100000,30);m.Config.NativeMechanics=true;
 foreach(string w in NativeItemMapper.Apply(m.Config))Console.WriteLine(w);
 TrainingDummy.Populate(m.Config,catalog,1);
 BattleResult result=BattleEngine.Simulate(m.Config);
 foreach(string w in result.Warnings)Console.WriteLine(w);
 BattleDamageSeries ds=Aggregator.BuildDamage(result,100000);
 foreach(double t in new[]{0.0,15.0,15.3,30.0})Console.WriteLine("t="+t+" damage="+ds.At(t).Damage+" burn="+ds.At(t).BurnDamage+" poison="+ds.At(t).PoisonDamage);
 foreach(BattleEvent e in result.Events)if(e.Time<6.01&&(e.Kind=="charge"||e.Kind=="fly"||e.Kind=="activate"))Console.WriteLine(e.Time+" "+e.Kind+" "+e.SourceItemId+" -> "+e.TargetItemId+" "+e.Value);
 foreach(LoadoutItem it in m.Config.Player.Items){CardDef c=m.Config.Cards[it.CardId];Console.Write(c.Name+":");foreach(BattleEvent e in result.Events)if(e.SourceItemId==c.Id&&e.Kind=="activate"&&e.Time<=15.3)Console.Write(" "+e.Time);Console.WriteLine();}
 }
}
