using TacticalRequests.Core;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
var peers = new HashSet<ulong> { 1, 2, 3 };
var targets = new Dictionary<uint, Target> { [10] = new(10, false, true, 0, 0, 0), [20] = new(20, true, true, 0, 0, 5, 1) };
var board = new Board();
Command Create(Need n, uint t) => new(Operation.Create, 0, n, t, default);
Command Respond(int id, Reply r) => new(Operation.Respond, id, default, 0, r);
Check(!board.Apply(99, Create(Need.Weak, 10), peers, targets, 0), "non-member rejected");
Check(!board.Apply(1, Create(Need.Block, 10), peers, targets, 0), "block requires ally");
Check(!board.Apply(1, Create(Need.Weak, 20), peers, targets, 0), "debuff requires enemy");
Check(board.Apply(1, Create(Need.Vulnerable, 10), peers, targets, 0), "targeted request");
int id = board.Requests.Single().Id;
Check(!board.Apply(1, Create(Need.Vulnerable, 10), peers, targets, 2), "duplicate suppressed");
Check(!board.Apply(1, Create(Need.Weak, 10), peers, targets, 0.5), "spam limited");
Check(board.Apply(2, Respond(id, Reply.Cannot), peers, targets, 2), "cannot recorded");
Check(board.Requests[0].Status == Status.Open, "one refusal does not reject for team");
Check(board.Apply(3, Respond(id, Reply.Take), peers, targets, 2), "another teammate claims");
Check(!board.Apply(2, Respond(id, Reply.Take), peers, targets, 2), "simultaneous claim has one winner");
Check(board.Apply(3, Respond(id, Reply.Wait), peers, targets, 3), "owner can release with wait");
Check(board.Requests[0].Owner == null && board.Requests[0].Status == Status.Open, "released request reopened");
Check(board.Apply(2, Respond(id, Reply.Take), peers, targets, 3), "new owner accepts");
Check(!board.Apply(3, new(Operation.Cancel, id, default, 0, default), peers, targets, 3), "only author may cancel");
Check(!board.Observe(targets, peers, true), "no false completion before debuff");
targets[10] = targets[10] with { Vulnerable = 2 };
Check(!board.Observe(targets, peers, true), "debuff totals alone do not prove teammate contribution");
Check(board.RecordEnemyResult(2, 10, Need.Vulnerable) && board.Requests[0].Status == Status.Done, "teammate debuff application completes");
Check(!board.Apply(3, Respond(id, Reply.Take), peers, targets, 4), "completed request immutable");
Check(board.Apply(1, Create(Need.Block, 20), peers, targets, 4), "ally block request");
var block = board.Requests.Last();
Check(!board.Observe(targets, peers, true) && block.Status == Status.Open, "existing block not completion");
targets[20] = targets[20] with { Block = 9 };
Check(board.Observe(targets, peers, true) && block.Status == Status.Done, "new block completes");
board.Apply(1, Create(Need.Weak, 10), peers, targets, 6);
var weak = board.Requests.Last();
board.Apply(2, Respond(weak.Id, Reply.Take), peers, targets, 6);
peers.Remove(2);
Check(board.Observe(targets, peers, true) && weak.Owner == null, "disconnect releases claim");
Check(board.Observe(targets, peers, false) && weak.Status == Status.Expired, "enemy turn expires requests");
board.Clear();
board.Apply(1, Create(Need.Weak, 10), peers, targets, 7);
Check(board.Requests[0].Id > weak.Id, "round clear never reuses request ids");
targets[10] = targets[10] with { Alive = false };
Check(board.Observe(targets, peers, true) && board.Requests[0].Status == Status.Expired, "dead target expires");
var snapshot = JsonSerializer.Deserialize<List<Request>>(JsonSerializer.Serialize(board.Requests))!;
Check(snapshot[0].Id == board.Requests[0].Id && snapshot[0].Status == Status.Expired, "snapshot roundtrip");
board.Clear(); targets[10] = targets[10] with { Alive = true };
for (int i = 0; i < 20; i++)
{
    board.Apply(1, Create(Need.Weak, 10), peers, targets, 10 + i * 2);
    board.Apply(1, new(Operation.Cancel, board.Requests.Last().Id, default, 0, default), peers, targets, 10 + i * 2);
}
Check(board.Requests.Count == Board.Limit, "bounded history");
board.Clear(); peers.Add(2);
Check(!board.Apply(2, Create(Need.Block, 20), peers, targets, 100), "cannot request support for another player");
Check(board.Apply(1, Create(Need.Draw, 20), peers, targets, 100), "self draw request accepted");
var draw = board.Requests.Last();
Check(!board.Observe(targets, peers, true), "coordination requests never auto-complete on unrelated block");
Check(!board.Apply(2, new(Operation.Complete, draw.Id, default, 0, default), peers, targets, 101), "only author confirms satisfaction");
Check(board.Apply(1, new(Operation.Complete, draw.Id, default, 0, default), peers, targets, 101), "manual completion for draw request");
board.Apply(1, Create(Need.Focus, 10), peers, targets, 102);
var focus = board.Requests.Last();
Check(board.Apply(2, Respond(focus.Id, Reply.Take), peers, targets, 102) && board.Apply(3, Respond(focus.Id, Reply.Take), peers, targets, 102), "focus permits multiple participants");
Check(focus.Owner == null && focus.Replies.Count == 2, "team requests do not imply sole owner");
var loadout = new WheelLoadout();
Check(loadout.Self.Length == 8 && loadout.Enemy.Length == 6, "eight self and six enemy slots");
loadout.Replace(true, 3, Need.Souls);
Check(loadout.Self[3] == Need.Souls && loadout.Self.Length == 8, "reserve command replaces slot");
loadout.Replace(true, 0, Need.Strength);
Check(loadout.Self[0] == Need.Strength && loadout.Self[2] == Need.RequestSl, "existing command swaps slots");
loadout.Replace(false, 0, Need.Heal);
Check(loadout.Enemy[0] == Need.Focus, "self command cannot enter enemy wheel");
loadout.Self = [Need.Block]; loadout.Validate();
Check(loadout.Self.SequenceEqual(Needs.Self), "invalid saved layout recovers defaults");
Check(Needs.Self[0] == Need.RequestSl && Needs.Self.Length == 8, "SL has first default slot");
board.Clear();
Check(board.Apply(1, Create(Need.RequestSl, 20), peers, targets, 200), "SL self request accepted");
var sl = board.Requests.Last();
Check(board.Apply(2, Respond(sl.Id, Reply.Take), peers, targets, 200) && board.Apply(3, Respond(sl.Id, Reply.Cannot), peers, targets, 200), "SL independent votes");
Check(sl.Owner == null && !board.Observe(targets, peers, true), "SL never executes or auto completes");
board.Clear();
board.Apply(1, Create(Need.Weak, 10), peers, targets, 201);
board.Apply(2, Create(Need.Weak, 10), peers, targets, 201);
var grouped = new Command(Operation.RespondGroup, board.Requests[0].Id, default, 0, Reply.Take);
Check(board.Apply(3, grouped, peers, targets, 202) && board.Requests.All(r => r.Owner == 3), "single group response claims both requests");
Check(!board.Apply(2, grouped, peers, targets, 202) && board.Requests.All(r => r.Owner == 3), "group competing claim rejected atomically");
Check(board.Apply(3, grouped with { Reply = Reply.Wait }, peers, targets, 203) && board.Requests.All(r => r.Owner == null), "group wait releases both requests");
Check(board.Apply(3, Respond(board.Requests[1].Id, Reply.Take), peers, targets, 204) && board.Requests.All(r => r.Owner == 3), "individual protocol response cannot split group ownership");
var migrated = new WheelLoadout { Self = [Need.Block, Need.Strength, Need.Draw, Need.Energy, Need.Heal, Need.Dexterity, Need.Protect, Need.BurstSupport] };
migrated.Validate();
Check(migrated.Self[0] == Need.RequestSl, "previous default upgrades to SL priority");
migrated.Replace(true, 0, Need.Souls); migrated.Validate();
Check(migrated.Self[0] == Need.Souls, "custom wheel is preserved");
Console.WriteLine($"{checks} checks passed");


Check(SnapshotValidation.IsValid(new()), "empty snapshot accepted");
Check(!SnapshotValidation.IsValid(null), "null snapshot rejected");
Check(!SnapshotValidation.IsValid(new() { null! }), "null entry rejected");
Check(!SnapshotValidation.IsValid(new() { new() { Id = 1, Replies = null! } }), "null replies rejected");
Check(!SnapshotValidation.IsValid(new() { new() { Id = 1 }, new() { Id = 1 } }), "duplicate ids rejected");
Check(!SnapshotValidation.IsValid(new() { new() { Id = 0 } }), "invalid id rejected");
Check(!SnapshotValidation.IsValid(new() { new() { Id = 1, Need = (Need)255 } }), "unknown need rejected");
Check(!SnapshotValidation.IsValid(new() { new() { Id = 1, Status = (Status)255 } }), "unknown status rejected");
Check(!SnapshotValidation.IsValid(new() { new() { Id = 1, Replies = new() { [2] = (Reply)255 } } }), "unknown reply rejected");
var effectsBoard = new Board();
var effectTargets = new Dictionary<uint, Target> { [10] = new(10, false, true, 2, 1, 8, Strength: 5) };
foreach (var need in new[] { Need.Vulnerable, Need.Weak, Need.ReduceStrength, Need.Amplify })
{
    effectsBoard.Clear();
    Check(!effectsBoard.RecordEnemyResult(2, 10, need), "effect before request earns no future credit " + need);
    effectsBoard.Apply(1, Create(need, 10), peers, effectTargets, 10);
    effectsBoard.Observe(effectTargets, peers, true);
    Check(effectsBoard.Requests.Single().Active, "existing state does not complete request " + need);
    Check(!effectsBoard.RecordEnemyResult(1, 10, need), "own effect does not satisfy solo request " + need);
    Check(!effectsBoard.RecordEnemyResult(2, 99, need), "wrong target does not satisfy request " + need);
    effectsBoard.Apply(2, Create(need, 10), peers, effectTargets, 10);
    effectsBoard.Apply(1, Create(Need.HoldFire, 10), peers, effectTargets, 12);
    Check(effectsBoard.RecordEnemyResult(2, 10, need), "teammate effect completes merged group " + need);
    Check(effectsBoard.Requests.Where(r => r.Need == need).All(r => r.Status == Status.Done && !r.AttackConfirmed)
        && effectsBoard.Requests.Single(r => r.Need == Need.HoldFire).Active, "completion is scoped and preserves focus marker " + need);
    Check(!effectsBoard.RecordEnemyResult(3, 10, need), "completed group ignores repeated results " + need);
}
effectsBoard.Clear();
effectsBoard.Apply(1, Create(Need.BreakBlock, 10), peers, effectTargets, 26);
Check(!effectsBoard.Observe(effectTargets, peers, true), "remaining enemy block keeps request open");
effectTargets[10] = effectTargets[10] with { Block = 0 };
Check(effectsBoard.Observe(effectTargets, peers, true), "zero enemy block completes request");
var dexterityDefault = new WheelLoadout();
Check(dexterityDefault.Self[6] == Need.Dexterity && !dexterityDefault.Self.Contains(Need.Protect), "default wheel offers dexterity instead of protect");
var oldProtectDefault = new WheelLoadout { Self = [Need.RequestSl, Need.Block, Need.Strength, Need.Draw, Need.Energy, Need.Heal, Need.Protect, Need.BurstSupport] };
oldProtectDefault.Validate();
Check(oldProtectDefault.Self.SequenceEqual(Needs.Self), "saved protect default migrates to dexterity");
Check(Needs.SelfPool.Contains(Need.Protect) && Needs.SelfPool.Distinct().Count() == Needs.SelfPool.Length, "custom protect remains valid without duplicate dexterity entries");
var focusBoard = new Board();
var focusTargets = new Dictionary<uint, Target> { [10] = new(10, false, true, 0, 0, 0), [11] = new(11, false, true, 0, 0, 0) };
focusBoard.Apply(1, Create(Need.Focus, 10), peers, focusTargets, 30);
focusBoard.Apply(1, Create(Need.Weak, 10), peers, focusTargets, 32);
focusBoard.Apply(2, Create(Need.Focus, 11), peers, focusTargets, 30);
Check(!focusBoard.RecordEnemyResult(1, 10, Need.Focus), "request author's own attack does not confirm focus");
Check(!focusBoard.RecordEnemyResult(3, 99, Need.Focus), "unrequested target does not confirm focus");
focusBoard.Apply(2, Create(Need.Focus, 10), peers, focusTargets, 32);
Check(focusBoard.RecordEnemyResult(1, 10, Need.Focus), "teammate attack completes a merged focus request");
Check(focusBoard.Requests.Where(r => r.Target == 10 && r.Need == Need.Focus).All(r => r.Status == Status.Done && r.AttackConfirmed)
    && focusBoard.Requests.Count(r => r.Active) == 2, "attack completion leaves other needs and other targets active");
Check(!focusBoard.RecordEnemyResult(3, 10, Need.Focus), "repeated attack does not complete focus twice");
var receivedFocus = System.Text.Json.JsonSerializer.Deserialize<List<Request>>(System.Text.Json.JsonSerializer.Serialize(focusBoard.Requests))!;
Check(SnapshotValidation.IsValid(receivedFocus) && receivedFocus.Count(r => r.AttackConfirmed) == 2, "snapshot round trip preserves attack confirmation");
Check(!SnapshotValidation.IsValid(new() { new() { Id = 1, Need = Need.Focus, AttackConfirmed = true } }), "open request cannot be attack-confirmed");
var music = Enum.GetValues<Reply>().ToDictionary(reply => reply, ReplyMusic.Create);
foreach (var (reply, pcm) in music)
{
    var samples = new short[pcm.Length / 2];
    for (int i = 0; i < samples.Length; i++)
        samples[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2));
    Check(samples.Length == (int)(ReplyMusic.SampleRate * 2.4), reply + " music lasts 2.4 seconds");
    Check(samples[0] == 0 && samples[^1] == 0 && samples.Any(value => Math.Abs((int)value) > 1000)
        && samples.All(value => Math.Abs((int)value) < 30000), reply + " music has silent boundaries, audible notes and headroom");
    Check(pcm.SequenceEqual(ReplyMusic.Create(reply)), reply + " music generation is deterministic");
}
Check(!music[Reply.Take].SequenceEqual(music[Reply.Cannot]) && !music[Reply.Take].SequenceEqual(music[Reply.Wait])
    && !music[Reply.Cannot].SequenceEqual(music[Reply.Wait]), "reply music phrases differ");
var chatSound = ReplyMusic.CreateChat();
Check(chatSound.Length == (int)(ReplyMusic.SampleRate * 0.65) * 2 && chatSound.Any(value => value != 0), "chat sound is a short audible phrase");
Check(chatSound.SequenceEqual(ReplyMusic.CreateChat()) && music.Values.All(pcm => !pcm.SequenceEqual(chatSound)), "chat sound is deterministic and distinct from replies");
if (args.Length == 2 && args[0] == "--music-preview")
{
    Directory.CreateDirectory(args[1]);
    foreach (var (reply, pcm) in music)
    {
        using var file = new BinaryWriter(File.Create(Path.Combine(args[1], reply + ".wav")));
        file.Write("RIFF"u8); file.Write(36 + pcm.Length); file.Write("WAVEfmt "u8);
        file.Write(16); file.Write((short)1); file.Write((short)1);
        file.Write(ReplyMusic.SampleRate); file.Write(ReplyMusic.SampleRate * 2);
        file.Write((short)2); file.Write((short)16); file.Write("data"u8); file.Write(pcm.Length);
        file.Write(pcm);
    }
}
var deathTargets = new Dictionary<uint, Target> { [1] = new(1, true, false, 0, 0, 0, 1), [2] = new(2, false, true, 0, 0, 0) };
var deathBoard = new Board();
Check(deathBoard.Apply(1, Create(Need.RequestSl, 1), peers, deathTargets, 0), "dead player can request SL");
Check(!deathBoard.Observe(deathTargets, peers, true) && deathBoard.Requests.Single().Active, "SL on dead player remains active");
Check(!deathBoard.Apply(1, Create(Need.Block, 1), peers, deathTargets, 2), "dead player cannot request block");
Check(!deathBoard.Apply(2, Create(Need.RequestSl, 1), peers, deathTargets, 2), "another player cannot request SL as dead teammate");
Check(deathBoard.Apply(1, Create(Need.Focus, 2), peers, deathTargets, 2), "dead player can request focus on live enemy");
Console.WriteLine($"{checks} total checks passed");
