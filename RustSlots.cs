using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RustSlots", "EchoChamber", "0.1.8")]
    [Description("Rust Slot Battle: a Scrap-powered battle slot with persistent bonus and key preferences.")]
    public class RustSlots : RustPlugin
    {
        const string Root = "RustSlots.UI", Perm = "rustslots.use";
        Settings cfg;
        Dictionary<ulong, State> players;
        readonly HashSet<ulong> opened = new HashSet<ulong>();
        readonly HashSet<ulong> keys = new HashSet<ulong>();
        readonly HashSet<ulong> dataDetails = new HashSet<ulong>();
        readonly HashSet<ulong> resetConfirm = new HashSet<ulong>();
        readonly Dictionary<ulong, float> lastInput = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, int> animationTokens = new Dictionary<ulong, int>();
        readonly Dictionary<int, List<int[]>> outcomes = new Dictionary<int, List<int[]>>();
        readonly System.Random random = new System.Random();
        [PluginReference] Plugin ImageLibrary;
        bool healthy;
        const int ScrapPerBet = 10;
        const string AssetBase = "https://raw.githubusercontent.com/EchoChamber1942/Rust-Slot-Battle/main/assets/";
        const string ImageBase = AssetBase + "animations/";
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
            public long PendingScrap, TotalPaidScrap, TotalBetScrap, MaxNetScrap, BonusPaidAtStart;
            public int Bet = 3, Mode, Stage, Pending, Stocks, Round, SetGames, Event, Games, Paid;
            public int GamesSinceBonus, BonusCount, HighestRound, TotalContinues, BonusStartGame;
            public double Rate;
            public bool Bonus, Replay, Spinning, Nav, NavCorrect = true;
            public int[] Stops = new int[3];
            public bool[] Stopped = new bool[3];
            public int[] Order = new[]{0,1,2};
            public int StopCount, Role;
            public string Scene = "", Message = "STARTで遊技開始";
            public List<string> BonusHistory = new List<string>();
            public Dictionary<string,string> Keys = new Dictionary<string,string> {
                {"start","f6"},{"left","f7"},{"center","f8"},{"right","f9"},{"bet","f10"},{"close","f11"}
            };
        }
        protected override void LoadDefaultConfig() { cfg = new Settings(); EnsureDefaultImages(); SaveConfig(); }
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
            EnsureDefaultImages();
            SaveConfig();
        }
        void EnsureDefaultImages()
        {
            string[] scenes={"ScientistBlue","ScientistYellow","ScientistGreen","ScientistRed"};
            string[][] files={
                new[]{"01_shadow.jpg","02_fade.jpg","03_reveal.jpg","04_emphasis.jpg"},
                new[]{"01_shadow.jpg","02_fade.jpg","03_reveal.jpg","04_emphasis.jpg"},
                new[]{"01_shadow.jpg","02_fade.jpg","03_reveal.jpg","04_emphasis.jpg"},
                new[]{"01_shadow.jpg","02_approach.jpg","03_reveal.jpg","04_emphasis.jpg"}
            };
            for(int s=0;s<scenes.Length;s++) {
                for(int i=0;i<4;i++) {
                    string key=scenes[s]+"_0"+(i+1);
                    if(!cfg.ImageUrls.ContainsKey(key))cfg.ImageUrls[key]=ImageBase+scenes[s]+"/"+files[s][i];
                }
                if(!cfg.ImageUrls.ContainsKey(scenes[s]))cfg.ImageUrls[scenes[s]]=cfg.ImageUrls[scenes[s]+"_04"];
            }
            if(!cfg.ImageUrls.ContainsKey("CabinetFrame"))cfg.ImageUrls["CabinetFrame"]=AssetBase+"ui/cabinet_frame.png";
            string[] sceneFiles={
                "Outpost","Supermarket","Airfield","OilRig","OmenStrong",
                "BradleyApproach","BradleyAttack","BradleyCounter","BradleyWin","BradleyLose",
                "PatrolHeliApproach","PatrolHeliAttack","PatrolHeliCounter","PatrolHeliWin","PatrolHeliLose",
                "ChinookApproach","ChinookAttack","ChinookCounter","ChinookWin","ChinookLose"
            };
            foreach(string scene in sceneFiles)if(!cfg.ImageUrls.ContainsKey(scene))cfg.ImageUrls[scene]=AssetBase+"scenes/"+scene+".jpg";
        }
        void Init()
        {
            permission.RegisterPermission(Perm, this);
            try {
                players = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong,State>>(Name);
                if (players == null) players = new Dictionary<ulong,State>();
                foreach(var state in players.Values) {
                    if(state.Keys==null)state.Keys=new State().Keys;
                    if(state.BonusHistory==null)state.BonusHistory=new List<string>();
                }
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
        void OnServerInitialized() { RegisterImages(); }
        void OnPluginLoaded(Plugin plugin) { if(plugin!=null && plugin.Name=="ImageLibrary")RegisterImages(); }
        void RegisterImages()
        {
            if(ImageLibrary==null) {
                Puts("ImageLibrary not found; animation images will use direct HTTPS URLs.");
                return;
            }
            foreach(var pair in cfg.ImageUrls) {
                if(string.IsNullOrEmpty(pair.Value))continue;
                ImageLibrary.Call("AddImage",pair.Value,"RustSlots."+pair.Key,0UL);
            }
        }
        string CachedImage(string key)
        {
            if(ImageLibrary==null)return null;
            return ImageLibrary.Call<string>("GetImage","RustSlots."+key,0UL);
        }
        void AddImage(CuiElementContainer ui,string parent,string name,string key,string min="0 0",string max="1 1")
        {
            string url;
            if(!cfg.ImageUrls.TryGetValue(key,out url) || string.IsNullOrEmpty(url))return;
            string png=CachedImage(key);
            var image=new CuiRawImageComponent {Color="1 1 1 1"};
            if(!string.IsNullOrEmpty(png) && png!="0")image.Png=png;
            else image.Url=url;
            ui.Add(new CuiElement {Name=name,Parent=parent,Components={image,new CuiRectTransformComponent{AnchorMin=min,AnchorMax=max}}});
        }
        void CancelAnimation(BasePlayer p)
        {
            if(p==null)return;
            int token;
            animationTokens.TryGetValue(p.userID,out token);
            animationTokens[p.userID]=token+1;
            CuiHelper.DestroyUi(p,Root+".RoleAnimation");
        }
        void ShowAnimationFrame(ulong userId,string scene,int frame,int token)
        {
            int current;
            if(!animationTokens.TryGetValue(userId,out current) || current!=token || !opened.Contains(userId))return;
            var p=BasePlayer.FindByID(userId);
            if(!Allowed(p))return;
            string root=Root+".RoleAnimation";
            CuiHelper.DestroyUi(p,root);
            var ui=new CuiElementContainer();
            AddImage(ui,Root+".Screen",root,scene+"_0"+frame);
            CuiHelper.AddUi(p,ui);
        }
        void PlayRoleAnimation(BasePlayer p,string scene)
        {
            if(p==null || string.IsNullOrEmpty(scene) || !scene.StartsWith("Scientist"))return;
            CancelAnimation(p);
            int token=animationTokens[p.userID];
            ulong userId=p.userID;
            ShowAnimationFrame(userId,scene,1,token);
            timer.Once(0.30f,()=>ShowAnimationFrame(userId,scene,2,token));
            timer.Once(0.70f,()=>ShowAnimationFrame(userId,scene,3,token));
            timer.Once(1.05f,()=>ShowAnimationFrame(userId,scene,4,token));
            timer.Once(1.90f,()=> {
                int current;
                if(animationTokens.TryGetValue(userId,out current) && current==token) {
                    var target=BasePlayer.FindByID(userId);
                    if(target!=null)CuiHelper.DestroyUi(target,Root+".RoleAnimation");
                }
            });
        }
        void Save() { if (healthy) Interface.Oxide.DataFileSystem.WriteObject(Name, players); }
        void OnServerSave() { Save(); }
        void Unload() { Save(); foreach(var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p,Root); }
        void OnPlayerDisconnected(BasePlayer p, string reason) { Close(p); lastInput.Remove(p.userID); animationTokens.Remove(p.userID); Save(); }
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
        void Close(BasePlayer p) { if(p==null)return; CancelAnimation(p); opened.Remove(p.userID); keys.Remove(p.userID); dataDetails.Remove(p.userID); resetConfirm.Remove(p.userID); CuiHelper.DestroyUi(p,Root); }
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
                s.TotalBetScrap+=cost;
            }
            s.Replay=false; s.Paid=0; s.Games++;
            if(!s.Bonus)s.GamesSinceBonus++;
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
            if(!s.Nav)PlayRoleAnimation(p,s.Scene);
        }
        [ConsoleCommand("slot.stop")]
        void CmdStop(ConsoleSystem.Arg arg)
        {
            int r;
            if(arg.Args==null || arg.Args.Length!=1 || !int.TryParse(arg.Args[0].ToString(),out r) || r<1 || r>3)return;
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
            if(s.StopCount==3) { CancelAnimation(p); s.Spinning=false; Settle(s); }
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
        void UpdateMaxNet(State s) { s.MaxNetScrap=Math.Max(s.MaxNetScrap,s.TotalPaidScrap-s.TotalBetScrap); }
        void RecordBonusEnd(State s)
        {
            if(s.BonusHistory==null)s.BonusHistory=new List<string>();
            long paid=Math.Max(0,s.TotalPaidScrap-s.BonusPaidAtStart);
            string record="G"+s.BonusStartGame+"  "+events[Math.Max(0,Math.Min(events.Length-1,s.Event))]+"  R"+s.Round+"  +"+paid+" SC";
            s.BonusHistory.Insert(0,record);
            if(s.BonusHistory.Count>8)s.BonusHistory.RemoveRange(8,s.BonusHistory.Count-8);
        }
        void Settle(State s)
        {
            s.Paid=s.Role==2?11:s.Role==3||s.Role==4?6:s.Role==5?2:0;
            long roleReward=(long)s.Paid*ScrapPerBet;
            s.PendingScrap+=roleReward; s.TotalPaidScrap+=roleReward; s.Replay=s.Role==1;
            UpdateMaxNet(s);
            s.Message=s.Role==0?"次のゲームへ":s.Role==1?"REPLAY：次ゲーム無料":symbols[s.Role==2?1:s.Role==3||s.Role==4?2:3]+"  +"+(s.Paid*ScrapPerBet)+" SC";
            bool stocked=s.Nav && s.NavCorrect && s.Role==1;
            if(stocked) { Stock(s); s.Message="ナビ矛盾！ 次回BB／継続ストック獲得"; }
            if(s.Bonus) {
                if(!stocked && Chance(s.Role==5?0.3:s.Role==4?0.15:s.Role==3?0.07:s.Role==2?0.01:0))Stock(s);
                s.SetGames++;
                s.Scene=eventIds[s.Event]+(s.SetGames==1?"Approach":s.SetGames<cfg.GamesPerSet/2?"Attack":"Counter");
                if(s.SetGames>=cfg.GamesPerSet) {
                    long setReward=(long)cfg.SetReward*ScrapPerBet;
                    s.PendingScrap+=setReward; s.TotalPaidScrap+=setReward;
                    UpdateMaxNet(s);
                    bool more=s.Stocks>0;
                    if(more)s.Stocks--; else more=Chance(s.Rate);
                    s.Scene=eventIds[s.Event]+(more?"Win":"Lose");
                    s.Message=(more?"継続！":"BB終了")+"  ROUND "+s.Round+"  セット報酬 +"+(cfg.SetReward*ScrapPerBet)+" SC";
                    if(more) { s.TotalContinues++; s.Round++; s.HighestRound=Math.Max(s.HighestRound,s.Round); s.SetGames=0; }
                    else { RecordBonusEnd(s); s.Bonus=false; s.Mode=1; s.Pending=0; s.Stage=1; }
                }
                return;
            }
            if(s.Role>=6) {
                s.Bonus=true; s.Mode=0; s.Pending=0; s.Round=1; s.SetGames=0;
                s.GamesSinceBonus=0; s.BonusCount++; s.HighestRound=Math.Max(s.HighestRound,1);
                s.BonusStartGame=s.Games; s.BonusPaidAtStart=s.TotalPaidScrap;
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
        [ConsoleCommand("slot.data")]
        void CmdData(ConsoleSystem.Arg arg)
        {
            var p=arg.Player(); if(!Input(p))return;
            resetConfirm.Remove(p.userID);
            if(!dataDetails.Add(p.userID))dataDetails.Remove(p.userID);
            Draw(p);
        }
        [ConsoleCommand("slot.reset")]
        void CmdReset(ConsoleSystem.Arg arg)
        {
            var p=arg.Player(); if(!Input(p))return;
            var s=Get(p);
            if(s.Spinning || s.Bonus) { SendReply(p,"回転中またはBB中はデータをリセットできません。"); return; }
            string action=arg.Args!=null && arg.Args.Length>0?arg.Args[0].ToString():"ask";
            if(action=="cancel") { resetConfirm.Remove(p.userID); Draw(p); return; }
            if(action!="confirm" || !resetConfirm.Contains(p.userID)) { resetConfirm.Add(p.userID); Draw(p); return; }
            s.Games=0; s.GamesSinceBonus=0; s.BonusCount=0; s.HighestRound=0;
            s.TotalPaidScrap=0; s.TotalBetScrap=0; s.MaxNetScrap=0; s.TotalContinues=0;
            s.BonusStartGame=0; s.BonusPaidAtStart=0;
            if(s.BonusHistory==null)s.BonusHistory=new List<string>(); else s.BonusHistory.Clear();
            s.Message="統計データをリセットしました";
            resetConfirm.Remove(p.userID); dataDetails.Remove(p.userID); Save(); Draw(p);
        }
        [ConsoleCommand("slot.keys")]
        void CmdKeys(ConsoleSystem.Arg arg) { var p=arg.Player(); if(!Input(p))return; if(!keys.Add(p.userID))keys.Remove(p.userID); Draw(p); }
        [ConsoleCommand("slot.preset")]
        void CmdPreset(ConsoleSystem.Arg arg)
        {
            var p=arg.Player(); if(!Input(p))return;
            string[] values=arg.Args!=null && arg.Args.Length>0 && arg.Args[0].ToString()=="numpad"?new[]{"numpad0","numpad1","numpad2","numpad3","numpad4","numpad5"}:new[]{"f6","f7","f8","f9","f10","f11"};
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
            ui.Add(new CuiPanel {Image={Color="0 0 0 0"},RectTransform={AnchorMin="0.025 0.035",AnchorMax="0.975 0.965"},CursorEnabled=true},"Overlay",Root);
            if(keys.Contains(p.userID)) {
                ui.Add(new CuiPanel {Image={Color="0.075 0.06 0.05 0.985"},RectTransform={AnchorMin="0.035 0.035",AnchorMax="0.965 0.965"}},Root);
                ui.Add(new CuiPanel {Image={Color="0.28 0.12 0.035 1"},RectTransform={AnchorMin="0.035 0.90",AnchorMax="0.965 0.965"}},Root);
                Label(ui,Root,"RUST SLOT BATTLE  /  SCRAP", "0.22 0.905","0.67 0.96",22);
                Button(ui,"戻る","slot.keys","0.76 0.91","0.87 0.955");
                Button(ui,"閉じる","slot.close","0.88 0.91","0.96 0.955");
                Label(ui,Root,"個別キー設定（F1で各bindを実行）","0.03 0.84","0.97 0.93",20);
                Button(ui,"F6～F11 初期設定","slot.preset default","0.05 0.75","0.48 0.82");
                Button(ui,"テンキー 0～5","slot.preset numpad","0.52 0.75","0.95 0.82");
                for(int i=0;i<6;i++)Label(ui,Root,"bind "+s.Keys[actions[i]]+" \""+commands[i]+"\"","0.03 "+(0.65-i*0.07).ToString(System.Globalization.CultureInfo.InvariantCulture),"0.97 "+(0.72-i*0.07).ToString(System.Globalization.CultureInfo.InvariantCulture),18);
                Label(ui,Root,"最後に F1で writecfg\n任意変更：チャット /slotkey left f3\n既存bindは上書きされます。変更前の割当を控えてください。\n設定保存だけではクライアントのキーは変わりません。\nESCの専用割当は行いません。閉じるはF11／ボタン。","0.04 0.06","0.96 0.29",16);
                CuiHelper.AddUi(p,ui); return;
            }
            bool showNav=s.Nav && s.Spinning;
            string screenColor=showNav?"0.34 0.23 0.015 1":s.Stage==3?"0.24 0.09 0.06 1":"0.10 0.16 0.20 1";
            // Dynamic content is drawn first; the transparent cabinet PNG is then layered over it.
            ui.Add(new CuiPanel {Image={Color="0.018 0.025 0.028 0.99"},RectTransform={AnchorMin="0.064 0.335",AnchorMax="0.229 0.895"}},Root,Root+".Counter");
            ui.Add(new CuiPanel {Image={Color=screenColor},RectTransform={AnchorMin="0.319 0.604",AnchorMax="0.802 0.873"}},Root,Root+".Screen");
            ui.Add(new CuiPanel {Image={Color="0.055 0.055 0.052 1"},RectTransform={AnchorMin="0.348 0.222",AnchorMax="0.774 0.538"}},Root,Root+".ReelBed");
            string scene=string.IsNullOrEmpty(s.Scene)?stageIds[s.Stage]:s.Scene, imageKey=scene, url;
            if(!cfg.ImageUrls.TryGetValue(imageKey,out url)) { imageKey=stageIds[s.Stage]; cfg.ImageUrls.TryGetValue(imageKey,out url); }
            if(!string.IsNullOrEmpty(url))AddImage(ui,Root+".Screen",Root+".SceneImage",imageKey);
            AddImage(ui,Root,Root+".CabinetFrame","CabinetFrame");
            Label(ui,Root,"RUST SLOTS / SCRAP","0.315 0.91","0.515 0.966",20);
            Button(ui,"データ","slot.data","0.535 0.914","0.603 0.96","0 0 0 0");
            Button(ui,"キー設定","slot.keys","0.615 0.914","0.728 0.96","0 0 0 0");
            Button(ui,"閉じる","slot.close","0.742 0.914","0.835 0.96","0 0 0 0");
            Label(ui,Root+".Counter","<color=#28E9F3>DATA COUNTER</color>","0.03 0.88","0.97 0.98",18);
            string counter="総ゲーム\n<color=#42E5F5>"+s.Games+"</color>\n\n現在ゲーム\n<color=#42E5F5>"+s.GamesSinceBonus+"</color>\n\nBB回数  <color=#FFD35A>"+s.BonusCount+"</color>\n最高ROUND  <color=#72F06A>"+s.HighestRound+"</color>\n\n累計払出\n<color=#FFD35A>"+s.TotalPaidScrap+" SC</color>";
            Label(ui,Root+".Counter",counter,"0.04 0.22","0.96 0.88",15);
            Button(ui,"詳細","slot.data","0.078 0.405","0.216 0.455","0.04 0.36 0.40 0.94");
            Button(ui,"リセット","slot.reset ask","0.078 0.345","0.216 0.395","0.40 0.16 0.08 0.94");
            Label(ui,Root+".Screen",s.Bonus?events[s.Event]:stages[s.Stage],"0.02 0.82","0.98 1",25);
            string order=string.Join(" ▶ ",s.Order.Select(x=>(x+1).ToString()).ToArray());
            string title=showNav?"押し順  "+order:s.Bonus?"ROUND "+s.Round+"  /  "+s.SetGames+" GAME":scene.StartsWith("Scientist")?"立ちはだかる科学者":scene=="OmenStrong"?"警戒！":"荒廃した世界で、生き残れ。";
            string tint=s.Role==1?colors[0]:s.Role==2?colors[1]:s.Role==3||s.Role==4?colors[2]:colors[3];
            if(showNav)tint="#FFE14D";
            Label(ui,Root+".Screen","<color="+tint+">"+title+"</color>","0.02 0.30","0.98 0.74",showNav?40:28);
            Label(ui,Root+".Screen",s.Message,"0.02 0.01","0.98 0.25",18);
            Label(ui,Root,"SC  "+ScrapBalance(p)+"   BET "+(s.Bet*ScrapPerBet)+" SC   PAY "+(s.Paid*ScrapPerBet)+" SC","0.38 0.198","0.75 0.224",15);
            Button(ui,"BET","slot.bet","0.257 0.438","0.313 0.54","0 0 0 0");
            Button(ui,"START","slot.start","0.25 0.14","0.315 0.24","0 0 0 0");
            double[] stopMin={0.382,0.506,0.646};
            double[] stopMax={0.472,0.596,0.736};
            for(int i=0;i<3;i++)Button(ui,"STOP "+(i+1),"slot.stop "+(i+1),XY(stopMin[i],0.087),XY(stopMax[i],0.18),"0 0 0 0");
            if(s.PendingScrap>0) Button(ui,"未受取 "+s.PendingScrap+" SC：受取","slot.claim","0.32 0.875","0.53 0.91","0.35 0.18 0.04 0.96");
            Label(ui,Root,"有効ライン："+(s.Bet==1?"中段1本":s.Bet==2?"横3本":"横3本＋斜め2本"),"0.38 0.178","0.75 0.198",11);
            if(dataDetails.Contains(p.userID))DrawDataDetails(ui,p,s);
            if(resetConfirm.Contains(p.userID))DrawResetConfirm(ui);
            CuiHelper.AddUi(p,ui);
            if(!dataDetails.Contains(p.userID) && !resetConfirm.Contains(p.userID))DrawReels(p);
        }
        void DrawDataDetails(CuiElementContainer ui,BasePlayer p,State s)
        {
            ui.Add(new CuiPanel {Image={Color="0.025 0.03 0.035 0.995"},RectTransform={AnchorMin="0.215 0.145",AnchorMax="0.985 0.93"}},Root,Root+".DataDetails");
            Label(ui,Root+".DataDetails","<color=#42E5F5>PLAY DATA</color>","0.05 0.88","0.75 0.98",28);
            Button(ui,"戻る","slot.data","0.82 0.84","0.96 0.90","0.04 0.36 0.40 1");
            long net=s.TotalPaidScrap-s.TotalBetScrap;
            string rate=s.BonusCount>0?((double)s.Games/s.BonusCount).ToString("0.0"):"---";
            string left="総回転       "+s.Games+" G\n現在ゲーム   "+s.GamesSinceBonus+" G\n投入SC       "+s.TotalBetScrap+"\n払出SC       "+s.TotalPaidScrap+"\n差SC         "+(net>=0?"+":"")+net;
            string right="BB回数       "+s.BonusCount+"\n初当り確率   1 / "+rate+"\n継続回数     "+s.TotalContinues+"\n最高ROUND    "+s.HighestRound+"\n最高差SC     +"+s.MaxNetScrap;
            Label(ui,Root+".DataDetails",left,"0.06 0.46","0.48 0.84",20);
            Label(ui,Root+".DataDetails",right,"0.52 0.46","0.94 0.84",20);
            string history=s.BonusHistory!=null && s.BonusHistory.Count>0?string.Join("\n",s.BonusHistory.Take(5).ToArray()):"履歴はまだありません";
            Label(ui,Root+".DataDetails","<color=#FFD35A>直近BB履歴</color>\n\n"+history,"0.08 0.10","0.92 0.44",17);
        }
        void DrawResetConfirm(CuiElementContainer ui)
        {
            ui.Add(new CuiPanel {Image={Color="0.08 0.045 0.03 0.995"},RectTransform={AnchorMin="0.34 0.34",AnchorMax="0.82 0.69"}},Root,Root+".ResetConfirm");
            Label(ui,Root+".ResetConfirm","<color=#FFCC66>統計データをリセットしますか？</color>\n\n所持SC・BB・REPLAY・ストック・キー設定は消えません。","0.06 0.36","0.94 0.90",19);
            Button(ui,"リセット実行","slot.reset confirm","0.39 0.37","0.56 0.43","0.55 0.12 0.06 1");
            Button(ui,"キャンセル","slot.reset cancel","0.60 0.37","0.77 0.43","0.22 0.24 0.26 1");
        }
        void DrawReels(BasePlayer p)
        {
            var s=Get(p); var ui=new CuiElementContainer(); string root=Root+".Reels"; CuiHelper.DestroyUi(p,root);
            ui.Add(new CuiPanel {Image={Color="0 0 0 0"},RectTransform={AnchorMin="0 0",AnchorMax="1 1"}},Root,root);
            double[] reelMin={0.35,0.497,0.645};
            double[] reelMax={0.473,0.620,0.771};
            double[] rowMin={0.225,0.330,0.435};
            double[] rowMax={0.330,0.435,0.535};
            for(int r=0;r<3;r++) {
                int pos=s.Spinning && !s.Stopped[r]?random.Next(21):s.Stops[r];
                for(int row=0;row<3;row++) {
                    int sym=strips[r][(pos+row)%21];
                    int visualRow=2-row;
                    Label(ui,root,"<color="+colors[sym]+">"+symbols[sym]+"</color>",XY(reelMin[r],rowMin[visualRow]),XY(reelMax[r],rowMax[visualRow]),18);
                }
            }
            CuiHelper.AddUi(p,ui);
        }
    }
}
