using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RustSlots", "EchoChamber", "0.1.5")]
    [Description("Rust Slot Battle: a Scrap-powered battle slot with persistent bonus and key preferences.")]
    public class RustSlots : RustPlugin
    {
        const string Root = "RustSlots.UI", Perm = "rustslots.use";
        Settings cfg;
        Dictionary<ulong, State> players;
        readonly HashSet<ulong> opened = new HashSet<ulong>();
        readonly HashSet<ulong> keys = new HashSet<ulong>();
        readonly Dictionary<ulong, float> lastInput = new Dictionary<ulong, float>();
        readonly Dictionary<int, List<int[]>> outcomes = new Dictionary<int, List<int[]>>();
        readonly System.Random random = new System.Random();
        bool healthy;
        const int ScrapPerBet = 10;
        ItemDefinition scrapDefinition;
        // 0 barrel/replay, 1 lantern, 2 apple, 3 scrap, 4 red seven, 5 black Rust.
        readonly int[][] strips = {
            new [] {0,1,2,0,1,3,0,1,4,0,2,1,0,3,1,0,2,5,0,1,2},
            new [] {1,0,2,1,0,3,1,0,4,2,0,1,3,0,2,1,0,5,1,0,2},
            new [] {2,1,0,2,1,3,0,1,4,0,1,2,0,3,1,2,0,5,1,0,2}
        };
        readonly int[][] lines = { new[]{0,0,0}, new[]{1,1,1}, new[]{2,2,2}, new[]{0,1,2}, new[]{2,1,0} };
        readonly string[] symbols = {"REPLAY", "LANTERN", "APPLE", "SCRAP", "RUST 7", "BLACK RUST"};
        readonly string[] colors = {"#55BBFF", "#FFDD55", "#77DD77", "#FF6666", "#FF4444", "#C5A7FF"};
        readonly string[] stages = {"アウトポスト", "スーパーマーケット", "エアフィールド", "オイルリグ"};
        readonly string[] stageIds = {"Outpost", "Supermarket", "Airfield", "OilRig"};
        readonly string[] events = {"ブラッドリーAPCとの闘い", "パトロールヘリ撃破", "チヌーク・ロッククレート争奪"};
        readonly string[] eventIds = {"Bradley", "PatrolHeli", "Chinook"};
        readonly string[] actions = {"start", "left", "center", "right", "bet", "close"};
        readonly string[] commands = {"slot.start", "slot.stop1", "slot.stop2", "slot.stop3", "slot.bet", "slot.close"};
        class Settings
        {
            public int MaxStocks = 10;
            public int GamesPerSet = 8;
            public int SetReward = 30;
            public float SpinRefreshSeconds = 0.25f;
            public bool EnableSounds = true;
            public float SoundSequenceGap = 0.12f;
            public string StartSound = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab";
            public string StopSound = "assets/bundled/prefabs/fx/notice/loot.drag.drop.fx.prefab";
            public string NavigationSound = "assets/prefabs/locks/keypad/effects/lock.code.updated.prefab";
            public string EncounterSound = "assets/bundled/prefabs/fx/notice/item.pickup.fx.prefab";
            public string PayoutSound = "assets/prefabs/deployable/vendingmachine/effects/vending-machine-purchase-human.prefab";
            public string BonusSound = "assets/prefabs/deployable/research table/effects/research-success.prefab";
            public string LoseSound = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";
            public Dictionary<string, string> ImageUrls = new Dictionary<string, string>();
        }
        class State
        {
            public int CurrencyVersion;
            public long PendingScrap;
            public int Bet = 3, Mode, Stage, Pending, Stocks, Round, SetGames, Event, Games, Paid;
            public double Rate;
            public bool Bonus, Replay, Spinning, Nav, NavCorrect = true;
            public int[] Stops = new int[3];
            public bool[] Stopped = new bool[3];
            public int[] Order = new[]{0,1,2};
            public int StopCount, Role;
            public string Scene = "", Message = "STARTで遊技開始";
            public Dictionary<string,string> Keys = new Dictionary<string,string> {
                {"start","f6"},{"left","f7"},{"center","f8"},{"right","f9"},{"bet","f10"},{"close","f11"}
            };
        }
        protected override void LoadDefaultConfig() { cfg = new Settings(); SaveConfig(); }
        protected override void SaveConfig() { Config.WriteObject(cfg, true); }
        protected override void LoadConfig()
        {
            base.LoadConfig(); cfg = Config.ReadObject<Settings>();
            if (cfg == null) throw new Exception("RustSlots config is empty");
            cfg.MaxStocks = Math.Max(1, Math.Min(100, cfg.MaxStocks));
            cfg.GamesPerSet = Math.Max(1, cfg.GamesPerSet);
            cfg.SetReward = Math.Max(0, Math.Min(100000, cfg.SetReward));
            cfg.SpinRefreshSeconds = Math.Max(0.2f, cfg.SpinRefreshSeconds);
            cfg.SoundSequenceGap = Math.Max(0.06f,Math.Min(0.5f,cfg.SoundSequenceGap));
            if (cfg.StartSound == null) cfg.StartSound = "";
            if (cfg.StopSound == null) cfg.StopSound = "";
            if (cfg.NavigationSound == null) cfg.NavigationSound = "";
            if (cfg.EncounterSound == null) cfg.EncounterSound = "";
            if (cfg.PayoutSound == null) cfg.PayoutSound = "";
            if (cfg.BonusSound == null) cfg.BonusSound = "";
            if (cfg.LoseSound == null) cfg.LoseSound = "";
            if (cfg.ImageUrls == null) cfg.ImageUrls = new Dictionary<string,string>();
            SaveConfig();
        }
        void Init()
        {
            permission.RegisterPermission(Perm, this);
            try {
                players = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong,State>>(Name);
                if (players == null) players = new Dictionary<ulong,State>();
                healthy = true;
            } catch (Exception e) { PrintError("Data load failed; play disabled, file preserved: " + e.Message); return; }
            scrapDefinition = ItemManager.FindItemDefinition("scrap");
            if (scrapDefinition == null) { healthy=false; PrintError("Scrap item definition unavailable"); return; }
            // Retain an audit copy of the obsolete virtual economy before migrating.
            if (players.Values.Any(x=>x.CurrencyVersion<1)) {
                Interface.Oxide.DataFileSystem.WriteObject(Name + "_BeforeScrapMigration", Interface.Oxide.DataFileSystem.ReadObject<object>(Name));
                foreach (ulong id in players.Keys.ToArray()) {
                    var old = players[id];
                    if(old.CurrencyVersion<1) players[id]=new State { CurrencyVersion=1, Keys=old.Keys ?? new State().Keys };
                }
                Save();
            }
            for (int i=0;i<8;i++) outcomes[i] = new List<int[]>();
            for (int a=0;a<21;a++) for(int b=0;b<21;b++) for(int c=0;c<21;c++) {
                var stop = new[]{a,b,c}; outcomes[Role(stop,3)].Add(stop);
            }
            foreach (var bucket in outcomes) if (bucket.Value.Count == 0) {
                healthy=false; PrintError("Missing outcome role " + bucket.Key); return;
            }
            timer.Every(cfg.SpinRefreshSeconds, Tick);
        }
        void Save() { if (healthy) Interface.Oxide.DataFileSystem.WriteObject(Name, players); }
        void OnServerSave() { Save(); }
        void Unload() { Save(); foreach(var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p,Root); }
        void OnPlayerDisconnected(BasePlayer p, string reason) { Close(p); lastInput.Remove(p.userID); Save(); }
        void OnPlayerDeath(BasePlayer p, HitInfo info) { Close(p); }
        State Get(BasePlayer p)
        {
            State s;
            if (!players.TryGetValue(p.userID,out s)) { s=new State {CurrencyVersion=1}; players[p.userID]=s; Save(); }
            return s;
        }
        int ScrapBalance(BasePlayer p) { return p.inventory.GetAmount(scrapDefinition.itemid); }
        void PlaySound(BasePlayer p,string prefab)
        {
            if(!cfg.EnableSounds || p==null || p.net==null || p.net.connection==null || string.IsNullOrEmpty(prefab))return;
            var effect=new Effect(prefab,p,0,Vector3.zero,Vector3.forward);
            EffectNetwork.Send(effect,p.net.connection);
        }
        void PlaySoundSequence(BasePlayer p,params string[] prefabs)
        {
            if(!cfg.EnableSounds || p==null || prefabs==null)return;
            ulong userId=p.userID;
            for(int i=0;i<prefabs.Length;i++) {
                string sound=prefabs[i];
                if(string.IsNullOrEmpty(sound))continue;
                if(i==0) { PlaySound(p,sound); continue; }
                float delay=cfg.SoundSequenceGap*i;
                timer.Once(delay,()=> {
                    var target=BasePlayer.FindByID(userId);
                    if(target!=null && target.IsConnected)PlaySound(target,sound);
                });
            }
        }
        void PlayStartCue(BasePlayer p,State s)
        {
            if(s.Nav)PlaySoundSequence(p,cfg.LoseSound,cfg.NavigationSound,cfg.NavigationSound,cfg.NavigationSound,cfg.StartSound);
            else if(s.Scene=="ScientistBlue")PlaySoundSequence(p,cfg.StartSound,cfg.EncounterSound);
            else if(s.Scene=="ScientistYellow")PlaySoundSequence(p,cfg.PayoutSound,cfg.EncounterSound,cfg.PayoutSound);
            else if(s.Scene=="ScientistGreen")PlaySoundSequence(p,cfg.EncounterSound,cfg.BonusSound,cfg.EncounterSound);
            else if(s.Scene=="ScientistRed")PlaySoundSequence(p,cfg.LoseSound,cfg.EncounterSound,cfg.LoseSound,cfg.EncounterSound);
            else if(!string.IsNullOrEmpty(s.Scene))PlaySoundSequence(p,cfg.EncounterSound,cfg.LoseSound,cfg.EncounterSound);
            else PlaySoundSequence(p,cfg.StartSound,cfg.StopSound);
        }
        void PlayResultCue(BasePlayer p,State s)
        {
            if(s.Scene=="OmenStrong")
                PlaySoundSequence(p,cfg.LoseSound,cfg.EncounterSound,cfg.LoseSound,cfg.EncounterSound);
            else if(!string.IsNullOrEmpty(s.Scene) && s.Scene.EndsWith("Lose"))
                PlaySoundSequence(p,cfg.LoseSound,cfg.LoseSound,cfg.StopSound);
            else if(s.Scene=="BonusConfirmed" || !string.IsNullOrEmpty(s.Scene) && s.Scene.EndsWith("Win"))
                PlaySoundSequence(p,cfg.BonusSound,cfg.PayoutSound,cfg.NavigationSound,cfg.BonusSound);
            else if(s.Paid>0)PlaySoundSequence(p,cfg.PayoutSound,cfg.NavigationSound);
            else if(s.Replay)PlaySoundSequence(p,cfg.NavigationSound,cfg.NavigationSound);
            else PlaySound(p,cfg.StopSound);
        }
        void ConsolidateScrap(BasePlayer p)
        {
            int stackLimit=Math.Max(1,scrapDefinition.stackable);
            var stacks=new List<Item>();
            stacks.AddRange(p.inventory.containerMain.itemList.Where(x=>x.info.itemid==scrapDefinition.itemid));
            stacks.AddRange(p.inventory.containerBelt.itemList.Where(x=>x.info.itemid==scrapDefinition.itemid));
            int targetIndex=0;
            while(targetIndex<stacks.Count) {
                var target=stacks[targetIndex];
                int sourceIndex=stacks.Count-1;
                while(target.amount<stackLimit && sourceIndex>targetIndex) {
                    var source=stacks[sourceIndex];
                    int moved=Math.Min(stackLimit-target.amount,source.amount);
                    if(moved<=0) { sourceIndex--; continue; }
                    target.amount+=moved;
                    target.MarkDirty();
                    if(moved==source.amount) {
                        source.Remove();
                        stacks.RemoveAt(sourceIndex);
                        sourceIndex--;
                    } else {
                        source.amount-=moved;
                        source.MarkDirty();
                        sourceIndex--;
                    }
                }
                targetIndex++;
            }
        }
        void DeliverScrap(BasePlayer p, State s)
        {
            if(!Allowed(p))return;
            ConsolidateScrap(p);
            if(s.PendingScrap<=0)return;
            int stackLimit=Math.Max(1,scrapDefinition.stackable);
            // Fill an existing stack directly before consuming an empty inventory slot.
            // Undelivered rewards remain persistent and are never dropped on the ground.
            for(int i=0;i<30 && s.PendingScrap>0;i++) {
                var existing=p.inventory.containerMain.itemList
                    .Concat(p.inventory.containerBelt.itemList)
                    .FirstOrDefault(x=>x.info.itemid==scrapDefinition.itemid && x.amount<stackLimit);
                if(existing!=null) {
                    int added=(int)Math.Min(s.PendingScrap,stackLimit-existing.amount);
                    existing.amount+=added;
                    existing.MarkDirty();
                    s.PendingScrap-=added;
                    Save();
                    continue;
                }
                int amount=(int)Math.Min(s.PendingScrap,stackLimit);
                var item=ItemManager.CreateByName("scrap",amount);
                if(item==null)break;
                bool delivered=item.MoveToContainer(p.inventory.containerMain,-1,false)
                    || item.MoveToContainer(p.inventory.containerBelt,-1,false);
                if(!delivered) { item.Remove(); break; }
                s.PendingScrap-=amount;
                Save();
            }
        }
        [ConsoleCommand("slot.claim")]
        void CmdClaim(ConsoleSystem.Arg arg) {
            var p=arg.Player(); if(!Input(p))return;
            DeliverScrap(p,Get(p)); Draw(p);
        }
        bool Allowed(BasePlayer p) { return healthy && p != null && p.IsConnected && !p.IsDead() && (p.IsAdmin || permission.UserHasPermission(p.UserIDString,Perm)); }
        bool Input(BasePlayer p)
        {
            if (!Allowed(p) || !opened.Contains(p.userID)) return false;
            float t; if(lastInput.TryGetValue(p.userID,out t) && Time.realtimeSinceStartup-t < 0.08f) return false;
            lastInput[p.userID]=Time.realtimeSinceStartup; return true;
        }
        [ChatCommand("slot")]
        void Open(BasePlayer p,string command,string[] args)
        {
            if(!Allowed(p)) { SendReply(p,"利用権限がないか、スロットが停止中です。"); return; }
            DeliverScrap(p, Get(p)); opened.Add(p.userID); keys.Remove(p.userID); Draw(p);
        }
        void Close(BasePlayer p) { if(p==null)return; opened.Remove(p.userID); keys.Remove(p.userID); CuiHelper.DestroyUi(p,Root); }
        [ConsoleCommand("slot.close")]
        void CmdClose(ConsoleSystem.Arg arg) { Close(arg.Player()); }
        [ConsoleCommand("slot.start")]
        void CmdStart(ConsoleSystem.Arg arg)
        {
            var p=arg.Player(); if(!Input(p))return; var s=Get(p); if(s.Spinning)return;
            DeliverScrap(p,s);
            int cost=s.Bet*ScrapPerBet;
            if(!s.Replay) {
                if(ScrapBalance(p)<cost) { s.Message="スクラップ不足：必要 "+cost+" SC"; Draw(p); return; }
                int taken=p.inventory.Take(null,scrapDefinition.itemid,cost);
                if(taken!=cost) {
                    s.PendingScrap+=Math.Max(0,taken); Save(); DeliverScrap(p,s);
                    s.Message="支払未成立：回収分を返却／未受取へ保存"; Draw(p); return;
                }
            }
            s.Replay=false; s.Paid=0; s.Games++;
            int target;
            if(!s.Bonus && ((s.Mode==3 && s.Pending<=1) || s.Stocks>0)) {
                if(s.Stocks>0)s.Stocks--; target=Chance(0.12)?7:6;
            } else {
                if(!s.Bonus && s.Mode==3)s.Pending--;
                double roll=random.NextDouble();
                target=roll<0.38?0:roll<0.67?1:roll<0.86?2:roll<0.91?3:roll<0.94?4:roll<0.98?5:0;
                // 0 miss, 1 replay, 2 lantern, 3 weak apple, 4 strong apple, 5 scrap.
            }
            var pool=outcomes[target];
            if (target>=6 && s.Bet<3) pool=pool.Where(x=>Role(x,s.Bet)==target).ToList();
            s.Stops=(int[])pool[random.Next(pool.Count)].Clone();
            s.Role=Role(s.Stops,s.Bet); s.Stopped=new bool[3]; s.StopCount=0; s.Spinning=true;
            s.Nav=s.Bet==3 && (s.Role==2 || s.Role==1) && Chance(0.22);
            s.NavCorrect=true; s.Order=new[]{0,1,2}.OrderBy(x=>random.Next()).ToArray();
            s.Scene=s.Role==1?"ScientistBlue":s.Role==2?"ScientistYellow":s.Role==3||s.Role==4?"ScientistGreen":s.Role==5?"ScientistRed":"";
            s.Message=s.Nav?"押し順ナビ  " + string.Join(" → ",s.Order.Select(x=>(x+1).ToString()).ToArray()):"STOPでリールを停止";
            PlayStartCue(p,s);
            Save(); Draw(p);
        }
        [ConsoleCommand("slot.stop")]
        void CmdStop(ConsoleSystem.Arg arg)
        {
            int r;
            if(arg.Args==null || arg.Args.Length!=1 || !int.TryParse(arg.Args[0],out r) || r<1 || r>3)return;
            StopReel(arg.Player(),r-1);
        }
        [ConsoleCommand("slot.stop1")]
        void CmdStop1(ConsoleSystem.Arg arg) { StopReel(arg.Player(),0); }
        [ConsoleCommand("slot.stop2")]
        void CmdStop2(ConsoleSystem.Arg arg) { StopReel(arg.Player(),1); }
        [ConsoleCommand("slot.stop3")]
        void CmdStop3(ConsoleSystem.Arg arg) { StopReel(arg.Player(),2); }
        void StopReel(BasePlayer p,int r)
        {
            if(!Input(p))return;
            var s=Get(p); if(!s.Spinning || s.Stopped[r])return;
            if(s.Order[s.StopCount]!=r)s.NavCorrect=false;
            s.StopCount++; s.Stopped[r]=true;
            if(s.StopCount==3) { s.Spinning=false; Settle(s); }
            if(s.Spinning)PlaySound(p,cfg.StopSound); else PlayResultCue(p,s);
            Save(); if(!s.Spinning)DeliverScrap(p,s); Draw(p);
        }
        [ConsoleCommand("slot.bet")]
        void CmdBet(ConsoleSystem.Arg arg) { var p=arg.Player(); if(!Input(p))return; var s=Get(p); if(s.Spinning || s.Replay || s.Bonus)return; s.Bet=s.Bet%3+1; Save(); Draw(p); }
        int Symbol(int[] stop,int reel,int row) { return strips[reel][(stop[reel]+row)%21]; }
        int Role(int[] stop,int bet)
        {
            int result=0;
            for(int l=0;l<5;l++) {
                if(bet==1 && l!=1 || bet==2 && l>2)continue;
                int sym=Symbol(stop,0,lines[l][0]);
                if(sym!=Symbol(stop,1,lines[l][1]) || sym!=Symbol(stop,2,lines[l][2]))continue;
                int role=sym==0?1:sym==1?2:sym==2?(l>=3?4:3):sym==4?6:sym==5?7:0;
                result=Math.Max(result,role);
            }
            bool scrap=Symbol(stop,0,1)==3 || bet>=2 && (Symbol(stop,0,0)==3 || Symbol(stop,0,2)==3);
            if(scrap && result<6)result=5;
            return result;
        }
        bool Chance(double probability) { return random.NextDouble()<probability; }
        void Stock(State s) { s.Stocks=Math.Min(cfg.MaxStocks,s.Stocks+1); }
        void Settle(State s)
        {
            s.Paid=s.Role==2?11:s.Role==3||s.Role==4?6:s.Role==5?2:0;
            s.PendingScrap+=(long)s.Paid*ScrapPerBet; s.Replay=s.Role==1;
            s.Message=s.Role==0?"次のゲームへ":s.Role==1?"REPLAY：次ゲーム無料":symbols[s.Role==2?1:s.Role==3||s.Role==4?2:3]+"  +"+(s.Paid*ScrapPerBet)+" SC";
            bool stocked=s.Nav && s.NavCorrect && s.Role==1;
            if(stocked) { Stock(s); s.Message="ナビ矛盾！ 次回BB／継続ストック獲得"; }
            if(s.Bonus) {
                if(!stocked && Chance(s.Role==5?0.3:s.Role==4?0.15:s.Role==3?0.07:s.Role==2?0.01:0))Stock(s);
                s.SetGames++;
                s.Scene=eventIds[s.Event]+(s.SetGames==1?"Approach":s.SetGames<cfg.GamesPerSet/2?"Attack":"Counter");
                if(s.SetGames>=cfg.GamesPerSet) {
                    s.PendingScrap+=(long)cfg.SetReward*ScrapPerBet;
                    bool more=s.Stocks>0;
                    if(more)s.Stocks--; else more=Chance(s.Rate);
                    s.Scene=eventIds[s.Event]+(more?"Win":"Lose");
                    s.Message=(more?"継続！":"BB終了")+"  ROUND "+s.Round+"  セット報酬 +"+(cfg.SetReward*ScrapPerBet)+" SC";
                    if(more) { s.Round++; s.SetGames=0; }
                    else { s.Bonus=false; s.Mode=1; s.Pending=0; s.Stage=1; }
                }
                return;
            }
            if(s.Role>=6) {
                s.Bonus=true; s.Mode=0; s.Pending=0; s.Round=1; s.SetGames=0;
                s.Event=s.Role==7?2:Chance(0.25)?1:0;
                s.Rate=s.Event==0?0.66:s.Event==1?0.79:0.84;
                if(s.Role==7) { Stock(s); if(Chance(0.05)) { s.Rate=0.89; s.Message="FREEZE 89%"; } }
                s.Scene="BonusConfirmed"; s.Message="BB開始  "+events[s.Event]+"  継続率 "+(s.Rate*100).ToString("0")+"%";
                return;
            }
            if(s.Mode!=3) {
                bool center=s.Role==5 && Symbol(s.Stops,0,1)==3;
                double bb=center?(s.Mode==2?1:0.25):s.Role==4?(s.Mode==2?0.35:0.08):0.002;
                if(Chance(bb)) { s.Mode=3; s.Pending=random.Next(6,33); }
                else if(Chance(s.Role==5?0.35:s.Role==4?0.25:s.Role==3?0.15:s.Role==2?0.02:s.Role==1?0.005:0))s.Mode=Math.Min(2,s.Mode+1);
                else if(s.Role==0 && Chance(0.04))s.Mode=Math.Max(0,s.Mode-1);
            }
            if(Chance(0.25)) {
                int expected=s.Mode==3?3:s.Mode;
                s.Stage=Math.Max(0,Math.Min(3,expected+random.Next(-1,2)));
                if(Chance(0.08))s.Stage=random.Next(4);
            }
            if(s.Mode==3 && s.Pending<6 || Chance(0.03))s.Scene="OmenStrong";
        }
        [ConsoleCommand("slot.keys")]
        void CmdKeys(ConsoleSystem.Arg arg) { var p=arg.Player(); if(!Input(p))return; if(!keys.Add(p.userID))keys.Remove(p.userID); Draw(p); }
        [ConsoleCommand("slot.preset")]
        void CmdPreset(ConsoleSystem.Arg arg)
        {
            var p=arg.Player(); if(!Input(p))return;
            string[] values=arg.Args!=null && arg.Args.Length>0 && arg.Args[0]=="numpad"?new[]{"numpad0","numpad1","numpad2","numpad3","numpad4","numpad5"}:new[]{"f6","f7","f8","f9","f10","f11"};
            var s=Get(p); for(int i=0;i<6;i++)s.Keys[actions[i]]=values[i]; Save(); Draw(p);
        }
        [ChatCommand("slotkey")]
        void SetKey(BasePlayer p,string command,string[] args)
        {
            if(!Allowed(p))return;
            if(args.Length!=2 || !actions.Contains(args[0])) { SendReply(p,"/slotkey start|left|center|right|bet|close キー名"); return; }
            string key=args[1].ToLowerInvariant();
            bool valid=key.Length==1 && char.IsLetterOrDigit(key[0]) && key[0]<128;
            int n; valid|=key.StartsWith("f") && int.TryParse(key.Substring(1),out n) && n>=2 && n<=12;
            valid|=key.StartsWith("numpad") && key.Length==7 && char.IsDigit(key[6]);
            if(!valid) { SendReply(p,"対応キー：英数字1文字、f2～f12、numpad0～9"); return; }
            var s=Get(p); if(s.Keys.Any(k=>k.Key!=args[0] && k.Value==key)) { SendReply(p,"同じキーが別操作に設定されています。"); return; }
            s.Keys[args[0]]=key; Save(); SendReply(p,"設定を保存。F1で実行：bind "+key+" \""+commands[Array.IndexOf(actions,args[0])]+"\" → writecfg");
            if(opened.Contains(p.userID))Draw(p);
        }
        void Tick()
        {
            foreach(ulong id in opened.ToArray()) {
                var p=BasePlayer.FindByID(id); if(!Allowed(p)) { if(p!=null)Close(p); else opened.Remove(id); continue; }
                if(Get(p).Spinning && !keys.Contains(id))DrawReels(p);
            }
        }
        void Label(CuiElementContainer ui,string parent,string text,string min,string max,int size=18)
        { ui.Add(new CuiLabel {Text={Text=text,FontSize=size,Align=TextAnchor.MiddleCenter},RectTransform={AnchorMin=min,AnchorMax=max}},parent); }
        void Button(CuiElementContainer ui,string text,string command,string min,string max,string color="0.25 0.28 0.32 1")
        { ui.Add(new CuiButton {Button={Command=command,Color=color},Text={Text=text,FontSize=16,Align=TextAnchor.MiddleCenter},RectTransform={AnchorMin=min,AnchorMax=max}},Root); }
        string XY(double x,double y) { return x.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture)+" "+y.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture); }
        void Draw(BasePlayer p)
        {
            var s=Get(p); CuiHelper.DestroyUi(p,Root); var ui=new CuiElementContainer();
            ui.Add(new CuiPanel {Image={Color="0.055 0.065 0.08 0.99"},RectTransform={AnchorMin="0.24 0.04",AnchorMax="0.76 0.96"},CursorEnabled=true},"Overlay",Root);
            Label(ui,Root,"RUST SLOT BATTLE  /  SCRAP", "0.02 0.94","0.75 1",20);
            Button(ui,"閉じる","slot.close","0.84 0.95","0.98 0.99");
            Button(ui,keys.Contains(p.userID)?"戻る":"キー設定","slot.keys","0.65 0.95","0.82 0.99");
            if(keys.Contains(p.userID)) {
                Label(ui,Root,"個別キー設定（F1で各bindを実行）","0.03 0.84","0.97 0.93",20);
                Button(ui,"F6～F11 初期設定","slot.preset default","0.05 0.75","0.48 0.82");
                Button(ui,"テンキー 0～5","slot.preset numpad","0.52 0.75","0.95 0.82");
                for(int i=0;i<6;i++)Label(ui,Root,"bind "+s.Keys[actions[i]]+" \""+commands[i]+"\"","0.03 "+(0.65-i*0.07).ToString(System.Globalization.CultureInfo.InvariantCulture),"0.97 "+(0.72-i*0.07).ToString(System.Globalization.CultureInfo.InvariantCulture),18);
                Label(ui,Root,"最後に F1で writecfg\n任意変更：チャット /slotkey left f3\n既存bindは上書きされます。変更前の割当を控えてください。\n設定保存だけではクライアントのキーは変わりません。\nESCの専用割当は行いません。閉じるはF11／ボタン。","0.04 0.06","0.96 0.29",16);
                CuiHelper.AddUi(p,ui); return;
            }
            bool showNav=s.Nav && s.Spinning;
            string screenColor=showNav?"0.34 0.23 0.015 1":s.Stage==3?"0.24 0.09 0.06 1":"0.10 0.16 0.20 1";
            ui.Add(new CuiPanel {Image={Color=screenColor},RectTransform={AnchorMin="0.035 0.46",AnchorMax="0.965 0.94"}},Root,Root+".Screen");
            string scene=string.IsNullOrEmpty(s.Scene)?stageIds[s.Stage]:s.Scene, url;
            if(!cfg.ImageUrls.TryGetValue(scene,out url))cfg.ImageUrls.TryGetValue(stageIds[s.Stage],out url);
            if(!string.IsNullOrEmpty(url))ui.Add(new CuiElement {Parent=Root+".Screen",Components={new CuiRawImageComponent{Url=url,Color="1 1 1 1"},new CuiRectTransformComponent{AnchorMin="0 0",AnchorMax="1 1"}}});
            Label(ui,Root+".Screen",s.Bonus?events[s.Event]:stages[s.Stage],"0.02 0.82","0.98 1",25);
            string order=string.Join(" ▶ ",s.Order.Select(x=>(x+1).ToString()).ToArray());
            string title=showNav?"押し順  "+order:s.Bonus?"ROUND "+s.Round+"  /  "+s.SetGames+" GAME":scene.StartsWith("Scientist")?"立ちはだかる科学者":scene=="OmenStrong"?"警戒！":"荒廃した世界で、生き残れ。";
            string tint=s.Role==1?colors[0]:s.Role==2?colors[1]:s.Role==3||s.Role==4?colors[2]:colors[3];
            if(showNav)tint="#FFE14D";
            Label(ui,Root+".Screen","<color="+tint+">"+title+"</color>","0.03 0.30","0.97 0.74",showNav?40:28);
            Label(ui,Root+".Screen",s.Message,"0.02 0.01","0.98 0.25",18);
            Label(ui,Root,"SC  "+ScrapBalance(p)+"   BET  "+(s.Bet*ScrapPerBet)+" SC   PAY  "+(s.Paid*ScrapPerBet)+" SC","0.03 0.39","0.97 0.45",19);
            Button(ui,"BET","slot.bet","0.035 0.025","0.17 0.095");
            Button(ui,"START","slot.start","0.19 0.025","0.38 0.095","0.55 0.2 0.07 1");
            for(int i=0;i<3;i++)Button(ui,"STOP "+(i+1),"slot.stop "+(i+1),XY(0.40+i*0.19,0.025),XY(0.57+i*0.19,0.095),"0.45 0.12 0.12 1");
            if(s.PendingScrap>0) Button(ui,"未受取 "+s.PendingScrap+" SC：受取","slot.claim","0.035 0.87","0.48 0.93");
            Label(ui,Root,"有効ライン："+(s.Bet==1?"中段1本":s.Bet==2?"横3本":"横3本＋斜め2本"),"0.03 0.10","0.97 0.145",14);
            CuiHelper.AddUi(p,ui); DrawReels(p);
        }
        void DrawReels(BasePlayer p)
        {
            var s=Get(p); var ui=new CuiElementContainer(); string root=Root+".Reels"; CuiHelper.DestroyUi(p,root);
            ui.Add(new CuiPanel {Image={Color="0.03 0.03 0.035 1"},RectTransform={AnchorMin="0.07 0.15",AnchorMax="0.93 0.39"}},Root,root);
            for(int r=0;r<3;r++) {
                int pos=s.Spinning && !s.Stopped[r]?random.Next(21):s.Stops[r];
                for(int row=0;row<3;row++) {
                    int sym=strips[r][(pos+row)%21];
                    Label(ui,root,"<color="+colors[sym]+">"+symbols[sym]+"</color>",XY(r/3.0, (2-row)/3.0),XY((r+1)/3.0,(3-row)/3.0),20);
                }
            }
            CuiHelper.AddUi(p,ui);
        }
    }
}
