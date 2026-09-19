using System.Diagnostics;
using System.Reflection;
using Bot;
using RedUtils;
using RedUtils.Math;
using RLBot.Flat;
using BallPrediction = RedUtils.BallPrediction;

// Executable regression fixtures and independent approximate plants, NOT RocketSim/contact or live-game results.
int passed = 0, failed = 0;
void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
void Near(float a, float b, float tolerance = 0.001f) => Check(float.IsFinite(a) && MathF.Abs(a-b) <= tolerance, $"{a} != {b}");
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
Car CarAt(int index, int team, Vec3 position, Vec3 velocity = default, float yaw = MathF.PI / 2, float boost = 50) =>
    new() { Index = index, Team = (uint)team, Location = position, Velocity = velocity, Rotation = new(0,yaw,0),
        Orientation = new Mat3x3(new(0,yaw,0)), IsGrounded = position.z < 30, Boost = boost };
void Set(Type type, string property, object? owner, object value) => type.GetProperty(property,
    BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(owner,value);
void World(float time, Ball ball, params Car[] cars)
{
    Set(typeof(Game), "Time", null, time);
    Set(typeof(Cars), "AllCars", null, cars.ToList());
    Set(typeof(Ball), "Location", null, ball.location);
    Set(typeof(Ball), "Velocity", null, ball.velocity);
    Set(typeof(Ball), "Prediction", null, new BallPrediction { Slices = Enumerable.Range(0,361).Select(i =>
        new BallSlice(time+i/120f, ball.location+ball.velocity*(i/120f), ball.velocity)).ToArray() });
}
Stardust Agent(int index, int team)
{
    var bot = new Stardust("regression");
    Set(typeof(RLBot.Manager.Bot), "Index", bot, index);
    Set(typeof(RLBot.Manager.Bot), "Team", bot, team);
    return bot;
}
JumpState Jump(bool jumped=false, bool doubled=false, bool dodged=false, float timeout=-1) =>
    new(new PlayerInfoT { AirState=AirState.InAir, HasJumped=jumped, HasDoubleJumped=doubled, HasDodged=dodged, DodgeTimeout=timeout });
Boost Pad(int i, Vec3 location, bool large=false) => new(i, new BoostPadT { Location=new Vector3T { X=location.x,Y=location.y,Z=location.z }, IsFullBoost=large });



Game.Initialize();
Drive? TravelDrive(Positioning p) => typeof(Positioning).GetFields(BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public)
    .Where(f=>typeof(Drive).IsAssignableFrom(f.FieldType)).Select(f=>f.GetValue(p) as Drive).FirstOrDefault(x=>x!=null);
Test("pace: full-field positioning delegates to original fast Drive", () => {
 var me=CarAt(0,0,new(0,-2000,17),new(0,1300,0),boost:20); World(10,new Ball(new(0,3200,100),Vec3.Zero),me);
 var bot=Agent(0,0); var p=new Positioning(new(0,2500,17)); p.Run(bot);
 var drive=TravelDrive(p);
 Check(drive!=null && drive.TargetSpeed>=2200 && drive.AllowDodges,"long rotation bypassed fast Drive/acceleration mechanics");
 Check(bot.Controller.Boost,"aligned long rotation suppresses usable boost");
});
Test("pace: original travel speed-flip is selected and survives airborne continuation", () => {
 var me=CarAt(0,0,new(0,-2000,17),new(0,1000,0),boost:0); World(10,new Ball(new(0,3500,100),Vec3.Zero),me);
 var bot=Agent(0,0); var p=new Positioning(new(0,2500,17)); bot.Action=p;
 Set(typeof(RUBot),"DeltaTime",bot,1f/120);
 for(int i=0;i<24;i++){ Set(typeof(Game),"Time",null,10+i/120f); p.Run(bot); }
 var drive=TravelDrive(p);
 Check(drive?.Action?.GetType().Name=="SpeedFlip","zero-boost straight travel never selected the existing speed flip");
 Check(!p.Interruptible,"wrapper hides committed travel action");
 me.IsGrounded=false; me.Location.z=70; p.Run(bot);
 Check(ReferenceEquals(bot.Action,p),"airborne travel replaced its speed flip with recovery");
});
Test("shots: contested ball is not a categorical opponent-ETA veto", () => {
 var me=CarAt(0,0,new(0,-1000,17),new(0,1000,0)); var enemy=CarAt(1,1,new(200,1100,17),yaw:-MathF.PI/2);
 World(10,new Ball(new(0,1000,100),Vec3.Zero),me,enemy); var bot=Agent(0,0);
 var original=Ball.Prediction.Slices.Select(s=>bot.DefaultShotCheck(s,new Target(bot.TheirGoal))).FirstOrDefault(s=>s!=null);
 Check(original!=null,"reference fixture has no original-solver shot");
 Check(ShootingPlanner.Select(bot,false,.1f,_=>false)!=null,"every original-solver shot was excluded just because opponent is near the ball");
});
Test("shots: search reaches the later feasible part of its advertised horizon", () => {
 var me=CarAt(0,0,new(0,-3000,17)); World(10,new Ball(new(0,1000,100),Vec3.Zero),me);
 var bot=Agent(0,0);
 Check(ShootingPlanner.Select(bot,false,6,_=>false)!=null,"early rejection budget starves later feasible contact");
});
Test("commit: solo bot takes a shot instead of choosing a support slot", () => {
 var me=CarAt(0,0,new(0,-1000,17),new(0,1000,0)); var enemy=CarAt(1,1,new(200,1100,17),yaw:-MathF.PI/2);
 World(10,new Ball(new(0,1000,100),Vec3.Zero),me,enemy); var bot=Agent(0,0); bot.Run();
 Check(bot.Action is Shot,$"solo action was {bot.Action?.GetType().Name}: {bot.Decision}");
});
Test("commit: no shot solution still closes the gap, not a stationary shadow slot", () => {
 var me=CarAt(0,0,new(0,-1000,17),new(0,1000,0)); var enemy=CarAt(1,1,new(200,1100,17),yaw:-MathF.PI/2);
 World(10,new Ball(new(0,1000,100),Vec3.Zero),me,enemy);
 Set(typeof(Ball),"Prediction",null,new BallPrediction { Slices=Array.Empty<BallSlice>() });
 var bot=Agent(0,0); bot.Run();
 Check(bot.Action is Positioning p && !p.Stop && p.Target.FlatDist(Ball.Location)<200,$"fallback retreats/parks: {bot.Decision}");
});
Test("boost: solo rotation actually selects a nearby full boost", () => {
 var me=CarAt(0,0,new(2900,-3700,17),boost:0); var enemy=CarAt(1,1,new(2500,3100,17),yaw:-MathF.PI/2);
 var pad=Pad(0,new(3072,-4096,73),true); Field.Boosts.Clear(); Field.Boosts.Add(pad);
 World(10,new Ball(new(0,3000,100),Vec3.Zero),me,enemy); var bot=Agent(0,0); bot.Run();
 Check(bot.Action is GetBoost g && g.BoostIndex==0,$"solo refused full-pad refill: {bot.Decision}");
});
Test("boost: refill uses original GetBoost and does not switch to parking next frame", () => {
 var me=CarAt(0,0,new(2900,-3700,17),boost:0); var enemy=CarAt(1,1,new(2500,3100,17),yaw:-MathF.PI/2);
 var pad=Pad(0,new(3072,-4096,73),true); Field.Boosts.Clear(); Field.Boosts.Add(pad);
 World(10,new Ball(new(0,3000,100),Vec3.Zero),me,enemy); var bot=Agent(0,0); bot.Run();
 var action=bot.Action;
 for(int i=1;i<=20;i++){ Set(typeof(Game),"Time",null,10+i/120f); bot.Run(); }
 Check(action is GetBoost && ReferenceEquals(bot.Action,action),"refill commitment is discarded by ordinary replanning");
});
Test("boost table: additional bot initialization does not create phantom pads", () => {
 var info=new FieldInfoT { BoostPads=new List<BoostPadT> { new() { Location=new Vector3T { X=3072,Y=-4096,Z=73 },IsFullBoost=true } } };
 Field.Initialize(info); Field.Initialize(info);
 Check(Field.Boosts.Count==1,$"shared pad table contains {Field.Boosts.Count} copies");
 Field.Update(new GamePacketT { BoostPads=new List<BoostPadStateT> { new() { IsActive=false,Timer=0 } } });
 Check(Field.Boosts.All(p=>!p.IsActive),"duplicate entry remains falsely active after pickup");
});
Test("pace: delegated speed flip emits the jump/dodge sequence", () => {
 var me=CarAt(0,0,new(0,-2000,17),new(0,1000,0),boost:0); World(10,new Ball(new(0,3500,100),Vec3.Zero),me);
 var bot=Agent(0,0); var p=new Positioning(new(0,2500,17)); bot.Action=p;
 Set(typeof(RUBot),"DeltaTime",bot,1f/120); float yaw=MathF.PI/2; bool jumped=false, dodged=false;
 for(int i=0;i<90;i++) {
   Set(typeof(Game),"Time",null,10+i/120f); bot.Controller=new ControllerStateT(); p.Run(bot);
   jumped |= bot.Controller.Jump;
   dodged |= bot.Controller.Jump && bot.Controller.Pitch < -.9f && MathF.Abs(bot.Controller.Roll)>.4f;
   // Minimal steering response just to let the unchanged speed-flip setup align.
   // This is an input-sequence test, not a physical flip-success simulation.
   yaw += bot.Controller.Steer * (1000/Drive.TurnRadius(1000)) / 120;
   me.Orientation=new Mat3x3(new(0,yaw,0)); me.Velocity=me.Forward*1000; me.Location+=me.Velocity/120;
   if(jumped) { me.IsGrounded=false; me.Location.z=70; }
 }
 Check(jumped && dodged,"travel selected a mechanic but never emitted its jump/dodge inputs");
});
Test("commit: a selected shot starts its original executor", () => {
 var me=CarAt(0,0,new(0,-1300,17),new(0,1100,0),boost:70); Field.Boosts.Clear();
 World(10,new Ball(new(0,0,100),Vec3.Zero),me); var bot=Agent(0,0);
 Set(typeof(RUBot),"DeltaTime",bot,1f/120); bot.Run();
 Check(bot.Action is Shot,"no selected shot"); var shot=(Shot)bot.Action!;
 shot.Run(bot); Check(!shot.Finished,"selected shot aborts on its very first execution");
 Check(bot.Controller.Throttle>0 || bot.Controller.Jump || bot.Controller.Boost,"shot executor issues no attacking input");
});
Test("commit: defensive pressure still produces a challenge", () => {
 var me=CarAt(0,0,new(0,-3500,17),new(0,1100,0),boost:70); var enemy=CarAt(1,1,new(150,-2100,17),yaw:-MathF.PI/2);
 Field.Boosts.Clear(); World(10,new Ball(new(0,-2000,100),Vec3.Zero),me,enemy); var bot=Agent(0,0); bot.Run();
 Check(bot.Action is Shot || (bot.Action is Positioning p && !p.Stop && p.Target.FlatDist(Ball.Location)<200),$"pressure causes passive shadow: {bot.Decision}");
});
Test("boost: imminent goal cancels an interruptible full-pad trip", () => {
 var me=CarAt(0,0,new(2900,-3700,17),boost:0); var enemy=CarAt(1,1,new(2500,3100,17),yaw:-MathF.PI/2);
 var pad=Pad(0,new(3072,-4096,73),true); Field.Boosts.Clear(); Field.Boosts.Add(pad);
 World(10,new Ball(new(0,3000,100),Vec3.Zero),me,enemy); var bot=Agent(0,0); bot.Run();
 Check(bot.Action is GetBoost,"fixture did not begin refilling");
 World(10.0084f,new Ball(new(0,-4700,100),new(0,-1800,0)),me,enemy); bot.Run();
 Check(bot.Action is not GetBoost && bot.Decision.StartsWith("defend"),"boost trip ignored a goal-bound ball");
});
Test("boost: pickup completion releases the refill commitment", () => {
 var me=CarAt(0,0,new(2900,-3700,17),boost:0); var enemy=CarAt(1,1,new(2500,3100,17),yaw:-MathF.PI/2);
 var pad=Pad(0,new(3072,-4096,73),true); Field.Boosts.Clear(); Field.Boosts.Add(pad);
 World(10,new Ball(new(0,3000,100),Vec3.Zero),me,enemy); var bot=Agent(0,0); bot.Run();
 Check(bot.Action is GetBoost,"fixture did not begin refilling");
 me.Boost=100; pad.Update(new BoostPadStateT { IsActive=false,Timer=0 }); Set(typeof(Game),"Time",null,10.1f); bot.Run();
 Check(bot.Action is not GetBoost,"completed pickup remains stuck on a boost action");
});
Test("options: experimental possession does not override baseline play by default", () => {
 string[] keys={"STARDUST_GROUND_CONTROL","STARDUST_AERIAL_CARRY","STARDUST_FLIP_RESETS"};
 var saved=keys.Select(Environment.GetEnvironmentVariable).ToArray();
 try {
  foreach(string key in keys) Environment.SetEnvironmentVariable(key,null);
  var options=new StardustOptions(); Check(!options.GroundControl && !options.AerialCarry && !options.FlipResets);
  foreach(string key in keys) Environment.SetEnvironmentVariable(key,"1");
  options=new StardustOptions(); Check(options.GroundControl && options.AerialCarry && options.FlipResets);
 } finally { for(int i=0;i<keys.Length;i++) Environment.SetEnvironmentVariable(keys[i],saved[i]); }
});
Console.WriteLine($"RECOVERY RESULT: {passed} passed, {failed} failed. Production behavior fixtures, not live games.");
Environment.ExitCode=failed==0?0:1;
