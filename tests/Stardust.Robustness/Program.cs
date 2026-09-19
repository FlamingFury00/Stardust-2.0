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

Test("drive: horizontal yaw, not pitch, gates thrust in both directions", () => {
    var car=CarAt(0,0,new(0,0,17),yaw:0);
    Near(DrivingSafety.AimAngle(car,new(1000,0,17)),0);
    Near(DrivingSafety.AimAngle(car,new(0,1000,17)),MathF.PI/2);
    Near(DrivingSafety.AimAngle(car,new(0,-1000,17)),MathF.PI/2);
    Near(DrivingSafety.AimAngle(car,new(-1000,0,17),true),0);
});
Test("drive: actual Drive.Run suppresses sideways boost", () => {
    var car=CarAt(0,0,new(0,0,17),new(1500,0,0),yaw:0);
    World(10,new Ball(new(0,2500,100),Vec3.Zero),car); var bot=Agent(0,0);
    var drive=new Drive(car,new(0,3000,0),2300,false,true); drive.Run(bot);
    Check(!bot.Controller.Boost);
});
Test("arrival: AllowFlipping=false survives Run", () => {
    var car=CarAt(0,0,new(0,-3500,17),new(0,1200,0)); World(10,new Ball(new(0,1000,100),Vec3.Zero),car);
    var bot=Agent(0,0); var arrive=new Arrive(car,new(0,1000,17),new Vec3(0,1,0),15,false);
    arrive.Run(bot); Check(!arrive.Drive.AllowDodges);
});
Test("navigation: mirrored brake before entering own net; never drift", () => {
    for(int team=0;team<2;team++) {
        int side=Field.Side(team); var car=CarAt(0,team,new(0,side*4300,17),new(0,side*2000,0),side*MathF.PI/2);
        var c=Navigation.Controls(car,new(500,side*4350,17),Vec3.Zero,team,true);
        Check(c.Throttle<0 && !c.Boost && !c.Handbrake);
    }
});
Test("navigation: post-side returns first stage forward, not through post", () => {
    foreach(int side in new[]{-1,1}) foreach(int x in new[]{-1,1}) {
        Vec3 p=DrivingSafety.GoalWaypoint(new(x*1600,side*5000,17),new(-x*450,side*4350,17));
        Near(p.x,x*1600); Check(p.y*side<=4400);
        p=DrivingSafety.GoalWaypoint(new(x*600,side*5500,17),new(-x*2000,0,17));
        Check(MathF.Abs(p.x)<=350 && p.y*side<4800);
    }
});
Test("navigation: speed decays at target and reserve prevents cruise boost", () => {
    Check(Navigation.ArrivalSpeed(150,2300,true)<500); Near(Navigation.ArrivalSpeed(50,2300,true),0);
    var car=CarAt(0,0,new(0,-2000,17),new(0,1300,0),boost:10);
    Check(!Navigation.Controls(car,new(0,1500,17),new(0,2000,100),0,false).Boost);
});
Test("navigation: 120/60/30 Hz straight return rollouts stay out of net", () => {
    foreach(float dt in new[]{1f/120,1f/60,1f/30}) foreach(int team in new[]{0,1}) {
        int side=Field.Side(team); var car=CarAt(0,team,new(0,side*2700,17),new(0,side*2200,0),side*MathF.PI/2);
        float maxDepth=0;
        for(int i=0;i<(int)(3/dt);i++) {
            var c=Navigation.Controls(car,new(0,side*4350,17),new(0,side*4800,100),team,true);
            float speed=car.Velocity.Dot(car.Forward);
            float accel=c.Throttle*speed<0 ? c.Throttle*Car.BrakeAccel : c.Throttle*MathF.Max(0,1600-1.03f*MathF.Abs(speed));
            if(c.Boost) accel+=Car.BoostAccel;
            speed=System.Math.Clamp(speed+accel*dt,-1000,2300);
            car.Velocity=car.Forward*speed; car.Location+=car.Velocity*dt;
            maxDepth=MathF.Max(maxDepth,car.Location.y*side);
        }
        Check(maxDepth<4750,$"overshot guard: {maxDepth}, dt={dt}");
    }
});
Test("roles: 3v3 forms a triangle with a deeper anchor", () => {
    for(int team=0;team<2;team++) {
        int side=Field.Side(team); var cars=new[]{CarAt(0,team,new(0,side*600,17)),CarAt(1,team,new(600,side*1800,17)),CarAt(2,team,new(-500,side*3000,17))};
        var plan=TeamPlanning.Assign(cars,new Ball(new(0,0,100),Vec3.Zero),team,c=>c.Index+1);
        Check(plan.Challenger==0 && plan.Anchor==2 && plan.Roles[1]==TeamRole.Support);
        Check(plan.Targets[1].FlatDist(plan.Targets[2])>1600);
        Check(plan.Targets[2].y*side>plan.Targets[1].y*side+900);
    }
});
Test("roles: all six car-order permutations agree even at fuzzy-tie boundaries", () => {
    var cars=new[]{CarAt(0,0,new(0,-900,17)),CarAt(1,0,new(0,-900,17)),CarAt(2,0,new(0,-900,17))};
    var ball=new Ball(new(0,0,100),Vec3.Zero);
    foreach(int a in new[]{0,1,2}) foreach(int b in new[]{0,1,2}) if(a!=b) {
        var p=TeamPlanning.Assign(new[]{cars[a],cars[b],cars[3-a-b]},ball,0,c=>1+c.Index*.06f);
        Check(p.Challenger==0 && p.Anchor==1);
    }
});
Test("roles: demolished teammate excluded and forced defender owns only one role", () => {
    var cars=new[]{CarAt(0,0,new(0,-800,17)),CarAt(1,0,new(0,-2000,17)),CarAt(2,0,new(0,-3000,17))};
    cars[0].IsDemolished=true;
    var p=TeamPlanning.Assign(cars,new Ball(new(0,0,100),Vec3.Zero),0,defender:2);
    Check(p.Challenger==2 && p.Anchor==1 && p.Roles.Count==2);
});
Test("roles: 2000 seeded mirrored allocations retain team symmetry and spacing", () => {
    var rng=new Random(2484);
    for(int i=0;i<2000;i++) {
        var cars=Enumerable.Range(0,3).Select(j=>CarAt(j,0,new((float)rng.NextDouble()*6000-3000,(float)rng.NextDouble()*8000-4000,17))).ToArray();
        var ball=new Ball(new((float)rng.NextDouble()*6000-3000,(float)rng.NextDouble()*8000-4000,100),Vec3.Zero);
        var blue=TeamPlanning.Assign(cars,ball,0,c=>1+c.Index*.25f);
        var orange=TeamPlanning.Assign(cars.Select(c=>CarAt(c.Index,1,new(-c.Location.x,-c.Location.y,17))),new Ball(new(-ball.location.x,-ball.location.y,100),Vec3.Zero),1,c=>1+c.Index*.25f);
        Check(blue.Challenger==orange.Challenger && blue.Anchor==orange.Anchor);
        foreach(int j in new[]{0,1,2}) { Near(blue.Targets[j].x,-orange.Targets[j].x); Near(blue.Targets[j].y,-orange.Targets[j].y); Check(MathF.Abs(blue.Targets[j].y)<=4450); }
        int support=blue.Roles.Single(x=>x.Value==TeamRole.Support).Key;
        Check(blue.Targets[support].FlatDist(blue.Targets[blue.Anchor])>900);
    }
});
Test("defense: pressure exists before predicted scoring trajectory", () => {
    for(int team=0;team<2;team++) {
        int side=Field.Side(team); var b=new Ball(new(700,side*2400,100),new(0,side*700,0));
        Check(Tactics.UnderPressure(b,new(0,side*5120,0),2));
        Check(!Tactics.UnderPressure(new Ball(new(0,-side*2000,100),Vec3.Zero),new(0,side*5120,0),3));
    }
});
Test("defense: select incoming-trajectory defender rather than chasing current ball", () => {
    var cars=new[]{CarAt(0,0,new(1000,-1000,17)),CarAt(1,0,new(0,-3900,17))};
    var slices=Enumerable.Range(1,120).Select(i=>new BallSlice(10+i/120f,new Vec3(0,-2800-i*20,100),new Vec3(0,-2400,0))).ToArray();
    Check(Tactics.SaveOwner(cars,0,slices,10,1)==1);
    cars[1].IsDemolished=true; Check(Tactics.SaveOwner(cars,0,slices,10,1)!=1);
});
Test("defense: actual planner interrupts possession on next pressure frame", () => {
    var me=CarAt(0,0,new(0,-3500,17)); var opponent=CarAt(1,1,new(300,-2300,17));
    World(10,new Ball(new(0,-2300,100),Vec3.Zero),me,opponent); var bot=Agent(0,0);
    bot.Action=new GroundDribble(); bot.Run();
    World(10.0084f,new Ball(new(0,-2350,100),new(0,-1200,0)),me,opponent);
    bot.Action=new GroundDribble(); bot.Run(); Check(bot.Action is not IPossessionAction); Check(bot.Decision.StartsWith("defend"));
});
Test("defense: last-back positioning never makes a speculative dodge", () => {
    var me=CarAt(0,0,new(200,-3000,17),new(0,1400,0)); World(10,new Ball(new(0,-1000,100),Vec3.Zero),me);
    var bot=Agent(0,0); new Positioning(new(500,-4400,17),true).Run(bot);
    Check(!bot.Controller.Jump && !bot.Controller.Handbrake);
});
Test("shots: target stays well inside posts and below crossbar for mirrored sweeps", () => {
    for(int team=0;team<2;team++) for(int x=-3000;x<=3000;x+=300) {
        int side=Field.Side(team); var car=CarAt(0,team,new(x,side*1000,17));
        var slice=new BallSlice(11,new Vec3(x,0,100),new Vec3(900,100,0));
        Vec3 aim=ShootingPlanner.ScoringPoint(car,slice,new Goal(1-team),Array.Empty<Car>(),10);
        Check(ControlMath.Finite(aim) && MathF.Abs(aim.x)<=580 && aim.z>=Ball.Radius && aim.z<Goal.Height-Ball.Radius);
    }
});
Test("shots: setup check rejects stale and impossible short windows", () => {
    World(10,new Ball(new(0,0,100),Vec3.Zero),CarAt(0,0,new(1200,-900,17)));
    var car=Cars.AllCars[0]; var shot=new GroundShot(car,new BallSlice(10.08f,new Vec3(0,0,100),Vec3.Zero),new(0,5120,220));
    Check(!ShootingPlanner.HasSetupTime(shot,car,10)); Check(!ShootingPlanner.HasSetupTime(shot,car,11));
});
Test("shots: canonical ground and bounce fixtures still produce legal shots", () => {
    foreach(float z in new[]{100f,180f,280f}) {
        var me=CarAt(0,0,new(0,-1300,17),new(0,1100,0),boost:70);
        World(10,new Ball(new(0,0,z),Vec3.Zero),me); var bot=Agent(0,0);
        var shot=ShootingPlanner.Select(bot,false,6,_=>false);
        Check(shot!=null,$"no shot at z={z}"); Check(shot!.IsValid(me) && ShootingPlanner.HasSetupTime(shot,me,10));
    }
});
Test("air: equal ballistic motion does not create artificial upward acceleration", () => {
    var car=CarAt(0,0,new(0,0,600),new(500,500,200)); Vec3 lane=new(0,1,0);
    Vec3 normal=ControlMath.Unit(lane*.45f+Vec3.Up*.89f,Vec3.Up);
    var ball=new Ball(car.Location+normal*128,car.Velocity);
    Vec3 a=AirControl.CarryAcceleration(car,ball,ball.Predict(.1f),.1f,lane);
    Check(a.Length()<.01f,$"spurious acceleration={a}");
});
Test("air: ascending faster than the ball commands relative braking", () => {
    var car=CarAt(0,0,new(0,0,600),new(0,500,300)); var ball=new Ball(new(0,58,715),new(0,500,0));
    Check(AirControl.CarryAcceleration(car,ball,ball.Predict(.1f),.1f,new(0,1,0)).z<0);
});
Test("air: minimum-burst height forecast prevents upward overshoot", () => {
    var car=CarAt(0,0,new(0,0,600),new(0,0,300)); car.Orientation=new Mat3x3(new(MathF.PI/2,0,0));
    Check(!AirControl.SafeBurst(car,new Ball(new(0,0,710),Vec3.Zero),Vec3.Up));
    Check(!AirControl.SafeBurst(car,new Ball(new(0,0,620),Vec3.Zero),Vec3.Up));
});
Test("air: thrust budget matches requested impulse with an independent minimum-burst plant", () => {
    foreach(float dt in new[]{1f/120,1f/60,1f/30}) foreach(float duty in new[]{.15f,.35f,.65f,.9f}) {
        var gate=new ThrustBudget(); float engineUntil=-1,onTime=0;
        for(int i=0;i<(int)(8/dt);i++) {
            float now=i*dt; bool command=gate.Step(now,AirControl.BoostAcceleration*duty,true,100);
            if(command && now>=engineUntil) engineUntil=now+AirControl.MinimumBoostBurst;
            bool actual=command || now<engineUntil;
            if(actual) onTime+=dt;
        }
        Check(MathF.Abs(onTime/8-duty)<.08f,$"duty={duty}, actual={onTime/8}, dt={dt}");
    }
});
Test("air: nonfinite inputs and loss of safe alignment suppress new thrust", () => {
    var gate=new ThrustBudget(); Check(!gate.Step(0,1000,true,float.NaN)); Check(!gate.Step(1,float.NaN,true,50));
    Check(!gate.Step(2,1000,false,50));
    var old=new BoostGate(); Check(!old.Step(0,1000,1,float.NaN,false));
});
Test("reset: expired first-jump window is evidence of spent availability", () => {
    var e=new ResetEvidence(); Check(!e.Observe(Jump(true,timeout:-1),false,false,800,0));
    Check(e.Observe(Jump(),true,true,800,.1f));
    Check(!new ResetEvidence().Observe(Jump(),true,true,800,.1f));
});
Test("reset: approach creates controlled closure instead of coasting at equal velocity", () => {
    var car=CarAt(0,0,new(0,1000,600),new(0,500,0)); var ball=new Ball(new(0,1000,850),car.Velocity);
    Check(AirControl.ResetApproach(car,ball).z>0);
});
Test("reset: automatic policy requires cover, fuel, offense and cooldown", () => {
    var me=CarAt(0,0,new(0,1000,600),boost:60); var ball=new Ball(new(0,1000,900),Vec3.Zero);
    var f=new TacticalFrame { FirstMan=0,HasCover=true,OpponentEta=3 };
    Check(ResetPolicy.Allowed(me,ball,f,0,true,10,9,false));
    Check(!ResetPolicy.Allowed(me,ball,f,0,false,10,9,false));
    Check(!ResetPolicy.Allowed(me,ball,f,0,true,10,11,false));
    f.HasCover=false; Check(!ResetPolicy.Allowed(me,ball,f,0,true,10,9,false));
    f.HasCover=true; me.Boost=20; Check(!ResetPolicy.Allowed(me,ball,f,0,true,10,9,false));
    me.Boost=60; ball.location.y=-1000; Check(!ResetPolicy.Allowed(me,ball,f,0,true,10,9,false));
});
Test("reset: telemetry does not equate attempt or acquisition to follow-through", () => {
    var t=new MechanicsTelemetry(); t.Record("attempt",false,0); t.Record("acquired",false,0); t.Record("abort",false,0);
    Check(t.ResetAttempts==1 && t.ResetAcquisitions==1 && t.ResetDodges==0 && t.ResetFollowThroughs==0 && t.ResetAborts==1);
});
Test("boost: refill before empty, include small pads, preserve anchor under pressure", () => {
    var me=CarAt(0,0,new(0,-1800,17),boost:40); var small=Pad(0,new(0,-2600,0));
    var large=Pad(1,new(100,-2400,0),true);
    Check(RoutePlanner.SelectBoost(me,new[]{small},new(0,0,100),new(0,-4000,17),0,.3f,(_,_)=>.5f,lastBack:true,pressured:true)==small);
    Check(RoutePlanner.SelectBoost(me,new[]{large},new(0,0,100),new(0,-4000,17),0,6,(_,_)=>.5f,lastBack:true)==null);
});
Test("boost: active-route ownership is deterministic and avoids inactive pads", () => {
    var a=CarAt(0,0,new(100,-1800,17),boost:10); var b=CarAt(1,0,new(-100,-1800,17),boost:10); var pad=Pad(0,new(0,-2800,0));
    Check(RoutePlanner.SelectBoost(a,new[]{pad},new(0,0,100),new(0,-4000,17),0,6,(_,_)=>.5f,teammates:new[]{b})==pad);
    Check(RoutePlanner.SelectBoost(b,new[]{pad},new(0,0,100),new(0,-4000,17),0,6,(_,_)=>.5f,teammates:new[]{a})==null);
    pad.Update(new BoostPadStateT{IsActive=false,Timer=1});
    Check(RoutePlanner.SelectBoost(a,new[]{pad},new(0,0,100),new(0,-4000,17),0,6)==null);
});
Test("boost: pressure still rejects a real lateral detour", () => {
    var me=CarAt(0,0,new(0,-2000,17),boost:10); var pad=Pad(0,new(1300,-2600,0));
    Check(RoutePlanner.SelectBoost(me,new[]{pad},new(0,0,100),new(0,-4000,17),0,.3f,(_,_)=>.5f,pressured:true)==null);
});
Test("navigation: 2D turn-limited returns avoid posts and converge to cover", () => {
    foreach(int team in new[]{0,1}) foreach(int flank in new[]{-1,1}) foreach(float dt in new[]{1f/120,1f/30}) {
        int side=Field.Side(team);
        foreach(Vec3 initial in new[]{new Vec3(flank*2300,side*3600,17),new Vec3(flank*1600,side*4800,17),new Vec3(flank*550,side*5500,17)}) {
            var car=CarAt(0,team,initial,new(0,side*800,0),side*MathF.PI/2,40);
            Vec3 target=new(-flank*450,side*4350,17);
            float yaw=side*MathF.PI/2, speed=800, closest=10000;
            for(int i=0;i<(int)(9/dt);i++) {
                var c=Navigation.Controls(car,target,Vec3.Zero,team,true);
                float accel=c.Throttle*speed<0 ? c.Throttle*Car.BrakeAccel : c.Throttle*MathF.Max(0,1600-1.03f*MathF.Abs(speed));
                if(c.Boost) accel+=Car.BoostAccel;
                speed=System.Math.Clamp(speed+accel*dt,-1000,2300);
                float rate=c.Steer*speed/Drive.TurnRadius(MathF.Abs(speed));
                yaw+=rate*dt; car.Orientation=new Mat3x3(new(0,yaw,0)); car.LocalAngularVelocity=new(0,0,rate);
                car.Velocity=car.Forward*speed; car.Location+=car.Velocity*dt;
                closest=MathF.Min(closest, MathF.Min(car.Location.FlatDist(new(893,side*5120,17)),car.Location.FlatDist(new(-893,side*5120,17))));
                Check(!c.Handbrake && !c.Jump);
            }
            Check(closest>150,$"post clearance {closest}; start={initial}; dt={dt}");
            Check(car.Location.FlatDist(target)<850,$"failed to reach cover {car.Location}; start={initial}");
            Check(car.Location.y*side<4850,$"retreated into net {car.Location}");
        }
    }
});
Test("air: ballistic tracking rollouts correct height error with a minimum-burst plant", () => {
    foreach(float dt in new[]{1f/120,1f/60,1f/30}) foreach(float verticalError in new[]{-50f,0f,50f}) {
        var car=CarAt(0,0,new(0,0,1000+verticalError),new(0,500,300));
        Vec3 lane=new(0,1,0), normal=ControlMath.Unit(lane*.45f+Vec3.Up*.89f,Vec3.Up);
        var ball=new Ball(new Vec3(0,0,1000)+normal*128,new(0,500,300));
        var gate=new ThrustBudget(); float engineUntil=-1, minimumGap=10000;
        for(int i=0;i<(int)(1.0f/dt);i++) {
            Vec3 a=AirControl.CarryAcceleration(car,ball,ball.Predict(.1f),.1f,lane);
            Vec3 nose=ControlMath.Unit(a,car.Forward);
            // Isolated translation test: perfect attitude, not a contact or orientation simulation.
            car.Forward=nose; car.Right=ControlMath.Unit(Vec3.Up.Cross(nose),Vec3.X); car.Up=nose.Cross(car.Right);
            bool command=gate.Step(i*dt,a.Dot(nose),AirControl.SafeBurst(car,ball,nose),100);
            if(command && i*dt>=engineUntil) engineUntil=i*dt+AirControl.MinimumBoostBurst;
            bool engine=command || i*dt<engineUntil;
            Vec3 thrust=engine?nose*AirControl.BoostAcceleration:Vec3.Zero;
            car.Location+=car.Velocity*dt+(Game.Gravity+thrust)*(.5f*dt*dt); car.Velocity+=(Game.Gravity+thrust)*dt;
            ball.location+=ball.velocity*dt+Game.Gravity*(.5f*dt*dt); ball.velocity+=Game.Gravity*dt;
            minimumGap=MathF.Min(minimumGap,ball.location.z-car.Location.z);
        }
        float error=(ball.location-normal*128-car.Location).Length();
        Check(error<MathF.Max(25,MathF.Abs(verticalError)),$"tracking error {error}; initial={verticalError}; dt={dt}");
        Check(minimumGap>50,$"passed above ball, minimum height gap {minimumGap}");
    }
});
Test("defense: actual three-agent decisions reserve distinct cover targets", () => {
    var cars=new[]{CarAt(0,0,new(0,-1300,17)),CarAt(1,0,new(700,-2200,17)),CarAt(2,0,new(-500,-3500,17)),CarAt(3,1,new(0,1000,17))};
    World(10,new Ball(new(300,0,100),new(0,-700,0)),cars);
    var agents=new[]{Agent(0,0),Agent(1,0),Agent(2,0)};
    foreach(var bot in agents) bot.Run();
    var plans=agents.Select(x=>x.Situation).ToArray();
    Check(plans.All(p=>p.FirstMan==plans[0].FirstMan));
    var positions=agents.Where(a=>a.Situation.FirstMan!=a.Index).Select(a=>a.Action).OfType<Positioning>().ToArray();
    Check(positions.Length==2);
    Check(positions[0].Target.FlatDist(positions[1].Target)>900);
});
Test("recovery: landing near a goalpost never requests a powerslide", () => {
    var car=CarAt(0,0,new(650,-4500,70),new(1000,0,-800));
    World(10,new Ball(new(0,0,100),Vec3.Zero),car); var bot=Agent(0,0);
    new Recover().Run(bot); Check(!bot.Controller.Handbrake);
});
Test("performance: bounded solver benchmark emits observational timings", () => {
    var cars=new[]{CarAt(0,0,new(2400,-1800,17),new(-300,600,0)),CarAt(1,0,new(-600,-3100,17)),CarAt(2,0,new(1100,-2800,17)),CarAt(3,1,new(-2400,1300,17))};
    World(10,new Ball(new(-1800,1400,800),new(600,-900,200)),cars); var bot=Agent(0,0);
    var measurements=new List<double>();
    for(int i=0;i<80;i++) { var sw=Stopwatch.StartNew(); _=Tactics.Evaluate(bot); _=ShootingPlanner.Select(bot,false,6,_=>false); measurements.Add(sw.Elapsed.TotalMilliseconds); }
    measurements.Sort();
    Console.WriteLine($"OBSERVED synthetic planning ms: p50={measurements[40]:F3}, p95={measurements[76]:F3}, max={measurements[^1]:F3}; includes warmup, not live latency.");
});

Console.WriteLine($"ROBUSTNESS RESULT: {passed} passed, {failed} failed. Synthetic fixtures/analytic plants only; no match-strength claim.");
Environment.ExitCode=failed==0?0:1;
