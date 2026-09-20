using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StressBotBenchmark.Network;
using System.Net.Http;

namespace StressBotBenchmark
{
    public class TibiaBot
    {
        private readonly string _name;
        private readonly string _password;
        private readonly BotConfig _config;
        private readonly BotMetrics _metrics;
        private readonly uint[] _xteaKey = new uint[4];
        private static readonly HttpClient _httpClient = new HttpClient();
        
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private bool _inWorld = false;

        public TibiaBot(string name, string password, BotConfig config, BotMetrics metrics)
        {
            _name = name;
            _password = password;
            _config = config;
            _metrics = metrics;
            var rand = new Random();
            for (int i = 0; i < 4; i++) _xteaKey[i] = (uint)rand.Next();
        }

        public string Name => _name;
        public bool InWorld => _inWorld;

        public DateTime LastWalkTime { get; private set; } = DateTime.MinValue;
        public DateTime LastSpellTime { get; private set; } = DateTime.MinValue;
        public DateTime LastAttackTime { get; private set; } = DateTime.MinValue;
        public DateTime LastDamageTakenTime { get; private set; } = DateTime.MinValue;
        
        public int TrackedMonstersTotal => _allSeenMonsters.Count;

        private bool _running = false;

        public async Task StartAsync()
        {
            _running = true;
            while (_running)
            {
                _cts = new CancellationTokenSource();
                try
                {
                    if (_config.UseApiLogin)
                    {
                        bool loggedIn = await ApiLoginAsync(_cts.Token);
                        if (!loggedIn)
                        {
                            if (!_config.Reconnect) break;
                            try { await Task.Delay(3000, _cts.Token); } catch { break; }
                            continue;
                        }
                    }
                    await ConnectAndRunAsync(_cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Console.WriteLine($"[Bot {_name}] Disconnected/Error: {e.Message}");
                    if (!_config.Reconnect) break;
                    _metrics.IncDisconnects();
                    _metrics.IncReconnects();
                    try { await Task.Delay(3000); } catch { }
                }
                finally
                {
                    _client?.Close();
                }
            }
        }

        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
            _client?.Close();
        }

        private async Task<bool> ApiLoginAsync(CancellationToken token)
        {
            string payload = $"{{\"emailOrUsername\":\"{_name}\",\"password\":\"{_password}\"}}";
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var response = await _httpClient.PostAsync(_config.ApiLoginUrl, content, token);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[API Error {_name}] HTTP {response.StatusCode} - {err}");
                }
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API Exception {_name}] {ex.Message}");
                return false;
            }
        }

        private async Task ConnectAndRunAsync(CancellationToken token)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_config.Host, _config.Port, token);
            _stream = _client.GetStream();
            _inWorld = false; // Reset

            byte[] challengeMsg = await ReadMessageAsync(token);
            if (challengeMsg.Length < 12 || challengeMsg[6] != 0x1F)
                throw new Exception("Invalid challenge received");
            
            uint ts = BitConverter.ToUInt32(challengeMsg, 7);
            byte rand = challengeMsg[11];

            await SendLoginMessageAsync(ts, rand, token);

            var readTask = ReadLoopAsync(token);
            var walkTask = WalkLoopAsync(token);
            var chatTask = ChatLoopAsync(token);
            var spellTask = SpellLoopAsync(token);
            var attackTask = AttackLoopAsync(token);

            Task completedTask = await Task.WhenAny(readTask, walkTask, chatTask, spellTask, attackTask);
            _cts?.Cancel();
            await completedTask; // Re-throw if the completed task failed!
        }

        private async Task SendLoginMessageAsync(uint ts, byte rand, CancellationToken token)
        {
            var rsaBytes = new OutputMessage();
            rsaBytes.AddU8(0);
            for (int i = 0; i < 4; i++) rsaBytes.AddU32(_xteaKey[i]);
            rsaBytes.AddU8(0); // MISSING BYTE FOUND in padding specification
            long tokenTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            rsaBytes.AddString($"{_name}\n{_password}\n\n{tokenTime}");
            rsaBytes.AddString(_name);
            rsaBytes.AddU32(ts);
            rsaBytes.AddU8(rand);
            
            byte[] rawRsa = rsaBytes.GetBuffer();
            byte[] paddedRsa = new byte[128];
            Array.Copy(rawRsa, paddedRsa, rawRsa.Length);
            byte[] encryptedRsa = Rsa.Encrypt(paddedRsa);

            var msg = new OutputMessage();
            msg.AddU16(3); // OS
            msg.AddU16(1098); // Protocol
            msg.AddBytes(new byte[7]);
            msg.AddBytes(encryptedRsa);

            var payload = msg.GetBuffer();
            byte[] final = new byte[payload.Length + 3];
            final[0] = (byte)((payload.Length + 1) & 0xFF);
            final[1] = (byte)(((payload.Length + 1) >> 8) & 0xFF);
            final[2] = 0x00;
            Array.Copy(payload, 0, final, 3, payload.Length);
            
            _metrics.AddBytesOut(final.Length);
            _metrics.IncSent();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _stream!.WriteAsync(final, token);
            sw.Stop();
            _metrics.AddDrainMs(sw.Elapsed.TotalMilliseconds);
        }



        private async Task<byte[]> ReadMessageAsync(CancellationToken token)
        {
            byte[] sizeHeader = new byte[2];
            int read = 0;
            while(read < 2) {
                int r = await _stream!.ReadAsync(sizeHeader, read, 2 - read, token);
                if (r == 0) throw new EndOfStreamException();
                read += r;
            }
            int size = sizeHeader[0] | (sizeHeader[1] << 8);
            byte[] body = new byte[size];
            read = 0;
            while(read < size) {
                int r = await _stream!.ReadAsync(body, read, size - read, token);
                if (r == 0) throw new EndOfStreamException();
                read += r;
            }
            _metrics.IncPacketsIn();
            _metrics.AddBytesIn((long)size + 2);
            return body;
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                byte[] body = await ReadMessageAsync(token);
                if (body.Length < 6) continue;
                
                byte[] encrypted = new byte[body.Length - 4];
                Array.Copy(body, 4, encrypted, 0, encrypted.Length);
                
                if (encrypted.Length % 8 != 0) continue;
                Xtea.Decrypt(encrypted, _xteaKey);
                
                var msg = new InputMessage(encrypted);
                ushort innerLen = msg.GetU16();
                int end = Math.Min(msg.Position + innerLen, encrypted.Length);
                var payload = new InputMessage(encrypted, msg.Position, end);
                
                await ProcessPayloadAsync(payload, token);
            }
        }

        private bool _fightModesSent = false;
        private readonly List<uint> _recentMonsters = new();
        private readonly HashSet<uint> _allSeenMonsters = new();
        private readonly object _monsterLock = new();

        private async Task ProcessPayloadAsync(InputMessage payload, CancellationToken token)
        {
            if (!_inWorld)
            {
                _inWorld = true;
                _fightModesSent = false;
            }

            // Fast heuristic scanner for Monster IDs (0x40000000 - 0x50000000)
            int length = payload.Remaining;
            int offset = payload.Position;
            byte[] buf = payload.Buffer;
            for (int i = 0; i <= length - 4; i++)
            {
                uint val = BitConverter.ToUInt32(buf, offset + i);
                // Tighten heuristic: 0x40XXXXXX where the 3rd byte is < 0x20.
                // This allows 2 million unique monster spawns per server restart, 
                // but mathematically filters out 99.9% of random map/string collisions!
                if (val >= 0x40000000 && val <= 0x401FFFFF)
                {
                    lock (_monsterLock) 
                    {
                        _allSeenMonsters.Add(val);
                        if (!_recentMonsters.Contains(val))
                        {
                            _recentMonsters.Add(val);
                            if (_recentMonsters.Count > 10) _recentMonsters.RemoveAt(0); // Keep last 10 unique IDs
                        }
                    }
                }
            }

            // Heuristic scanner for taking damage ("You lose ") and dealing damage ("your attack")
            // 'Y'=89, 'o'=111, 'u'=117, ' '=32, 'l'=108, 'o'=111, 's'=115, 'e'=101, ' '=32
            // 'y'=121, 'o'=111, 'u'=117, 'r'=114, ' '=32,  'a'=97,  't'=116, 't'=116, 'a'=97, 'c'=99, 'k'=107
            for (int i = 0; i <= length - 11; i++)
            {
                if (buf[offset + i] == 89 && buf[offset + i + 1] == 111 && buf[offset + i + 2] == 117 &&
                    buf[offset + i + 3] == 32 && buf[offset + i + 4] == 108 && buf[offset + i + 5] == 111 &&
                    buf[offset + i + 6] == 115 && buf[offset + i + 7] == 101 && buf[offset + i + 8] == 32)
                {
                    LastDamageTakenTime = DateTime.UtcNow;
                }

                if (buf[offset + i] == 121 && buf[offset + i + 1] == 111 && buf[offset + i + 2] == 117 &&
                    buf[offset + i + 3] == 114 && buf[offset + i + 4] == 32 && buf[offset + i + 5] == 97 &&
                    buf[offset + i + 6] == 116 && buf[offset + i + 7] == 116 && buf[offset + i + 8] == 97 &&
                    buf[offset + i + 9] == 99 && buf[offset + i + 10] == 107)
                {
                    LastAttackTime = DateTime.UtcNow;
                }
            }

            while (payload.Remaining > 0)
            {
                byte op = payload.GetU8();
                if (op == 0x1D) // PING
                {
                    await SendPingBackAsync(token);
                }
            }
        }

        private async Task SendPingBackAsync(CancellationToken token)
        {
            var msg = new OutputMessage();
            msg.AddU8(0x1E); // PINGBACK
            await SendRawGameMessageAsync(msg, token);
            _metrics.IncPingbacks();
        }

        private async Task SendRawGameMessageAsync(OutputMessage msg, CancellationToken token)
        {
            byte[] inner = msg.GetBuffer();
            var innerWrapper = new OutputMessage();
            innerWrapper.AddU16((ushort)inner.Length);
            innerWrapper.AddBytes(inner);
            byte[] toEncrypt = innerWrapper.GetBuffer();

            int pad = (8 - toEncrypt.Length % 8) % 8;
            byte[] padded = new byte[toEncrypt.Length + pad];
            Array.Copy(toEncrypt, padded, toEncrypt.Length);
            Xtea.Encrypt(padded, _xteaKey);

            byte[] packet = new byte[padded.Length + 6];
            packet[0] = (byte)((padded.Length + 4) & 0xFF);
            packet[1] = (byte)(((padded.Length + 4) >> 8) & 0xFF);
            
            uint checksum = Adler32(padded);
            packet[2] = (byte)(checksum & 0xFF);
            packet[3] = (byte)((checksum >> 8) & 0xFF);
            packet[4] = (byte)((checksum >> 16) & 0xFF);
            packet[5] = (byte)((checksum >> 24) & 0xFF);
            
            Array.Copy(padded, 0, packet, 6, padded.Length);

            await _writeLock.WaitAsync(token);
            try
            {
                await _stream!.WriteAsync(packet, token);
                _metrics.IncSent();
                _metrics.AddBytesOut(packet.Length);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte val in data)
            {
                a = (a + val) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private async Task WalkLoopAsync(CancellationToken token)
        {
            if (!_config.EnableRandomWalk) { await Task.Delay(-1, token); return; }
            var rand = new Random();
            byte[] walks = { 0x65, 0x66, 0x67, 0x68 };
            await Task.Delay(rand.Next(100, 3000), token); // Initial spawn spread

            while (!token.IsCancellationRequested)
            {
                int jitter = rand.Next((int)(-(_config.WalkIntervalMs * 0.2)), (int)(_config.WalkIntervalMs * 0.2));
                await Task.Delay((int)_config.WalkIntervalMs + jitter, token);
                if (!_inWorld) continue;

                // Only pause random wandering if ChaseMode is ENFORCED AND we are actively fighting!
                if (_config.EnableChaseMode && (DateTime.UtcNow - LastAttackTime).TotalSeconds < 5) continue;
                
                var msg = new OutputMessage();
                msg.AddU8(walks[rand.Next(walks.Length)]);
                await SendRawGameMessageAsync(msg, token);
                _metrics.IncWalks();
                LastWalkTime = DateTime.UtcNow;
            }
        }

        private async Task ChatLoopAsync(CancellationToken token)
        {
            if (!_config.EnableChat) { await Task.Delay(-1, token); return; }
            Random rand = new Random();
            await Task.Delay(rand.Next(500, 4000), token);

            while (!token.IsCancellationRequested)
            {
                int jitter = rand.Next((int)(-(_config.ChatIntervalMs * 0.2)), (int)(_config.ChatIntervalMs * 0.2));
                await Task.Delay((int)_config.ChatIntervalMs + jitter, token);
                if (!_inWorld) continue;
                
                var msg = new OutputMessage();
                msg.AddU8(0x96); // TALK
                msg.AddU8(1);
                msg.AddString($"Hello_im_csharp_bot_{_name}");
                await SendRawGameMessageAsync(msg, token);
                _metrics.IncChats();
            }
        }

        private async Task SpellLoopAsync(CancellationToken token)
        {
            if (!_config.EnableSpell) { await Task.Delay(-1, token); return; }
            Random rand = new Random();
            await Task.Delay(rand.Next(200, 2000), token);

            while (!token.IsCancellationRequested)
            {
                int jitter = rand.Next((int)(-(_config.SpellIntervalMs * 0.2)), (int)(_config.SpellIntervalMs * 0.2));
                await Task.Delay((int)_config.SpellIntervalMs + jitter, token);
                if (!_inWorld) continue;
                
                var msg = new OutputMessage();
                msg.AddU8(0x96); // TALK
                msg.AddU8(1);
                msg.AddString(_config.SpellText);
                await SendRawGameMessageAsync(msg, token);
                _metrics.IncSpells();
                LastSpellTime = DateTime.UtcNow;
            }
        }

        private async Task AttackLoopAsync(CancellationToken token)
        {
            if (!_config.EnableAttack) { await Task.Delay(-1, token); return; }
            Random rand = new Random();
            await Task.Delay(rand.Next(1000, 4000), token);

            while (!token.IsCancellationRequested)
            {
                int jitter = rand.Next((int)(-(_config.AttackScanIntervalMs * 0.2)), (int)(_config.AttackScanIntervalMs * 0.2));
                await Task.Delay((int)_config.AttackScanIntervalMs + jitter, token);
                if (!_inWorld) continue;

                if (!_fightModesSent)
                {
                    var modeMsg = new OutputMessage();
                    modeMsg.AddU8(0xA0); // CHANGE FIGHT MODES
                    modeMsg.AddU8(_config.FightMode); // 1 = Offensive, etc
                    modeMsg.AddU8((byte)(_config.EnableChaseMode ? 1 : 0)); // 1 = Chase, 0 = Stand
                    modeMsg.AddU8((byte)(_config.SafeFight ? 1 : 0));
                    await SendRawGameMessageAsync(modeMsg, token);
                    _fightModesSent = true;
                }

                uint targetId = 0;
                lock (_monsterLock)
                {
                    if (_recentMonsters.Count > 0)
                    {
                        targetId = _recentMonsters[rand.Next(_recentMonsters.Count)];
                    }
                }

                if (targetId != 0)
                {
                    var attackMsg = new OutputMessage();
                    attackMsg.AddU8(0xA1); // ATTACK
                    attackMsg.AddU32(targetId);
                    await SendRawGameMessageAsync(attackMsg, token);
                    _metrics.IncAttacks();
                }
            }
        }
    }
}
