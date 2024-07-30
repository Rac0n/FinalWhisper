using System;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Abstractions;
using NAudio.Wave;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using WebRtcVadSharp;
using System.Linq;

namespace AppDemo {

    class Program
    {
        const string DESTINATION = "6753@172.20.252.26:5060";
        private static int vadCheckFrequency = 80;

        private static readonly WaveFormat _waveFormat = new(8000, 16, 1);
        private static WaveFileWriter _waveFile;
        private static SIPUserAgent userAgent;
        private static byte[] audioVadCheck= Array.Empty<byte>();

        static async Task Main()
        {
            var outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "NAudio");
            Directory.CreateDirectory(outputFolder);
            var outputFilePath = Path.Combine(outputFolder, "output.mp3");
            _waveFile = new WaveFileWriter(outputFilePath, _waveFormat);


            var sipTransport = new SIPTransport();
            userAgent = new(sipTransport, null);

            WindowsAudioEndPoint winAudio = new WindowsAudioEndPoint(new AudioEncoder());
            var voipMediaSession = new VoIPMediaSession(winAudio.ToMediaEndPoints());
            voipMediaSession.OnRtpPacketReceived += OnRtpPacketReceived;

            userAgent.ClientCallFailed += (uac, err, resp) =>
            {
                Console.WriteLine($"Call failed {err}");
                _waveFile?.Close();
            };
            userAgent.OnCallHungup += (dialog) => _waveFile?.Close();

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                if (userAgent.IsCallActive)
                {
                    Console.WriteLine("Hanging up.");
                    userAgent.Hangup();
                }
                else
                {
                    Console.WriteLine("Cancelling call");
                    userAgent.Cancel();
                }
            };

            bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
            Console.WriteLine($"Call result {(callResult ? "success" : "failure")}.");

            Console.WriteLine("Press any key to hangup and exit.");
            Console.ReadLine();

            if (userAgent.IsCallActive)
            {
                Console.WriteLine("Hanging up.");
                userAgent.Hangup();
            }


            _waveFile?.Close();

            // Clean up.
            sipTransport.Shutdown();
        }

        private static bool DoesFrameContainSpeech(byte[] audioFrame)
        {
            using var vad = new WebRtcVad()
            {
                OperatingMode = OperatingMode.Aggressive,
                FrameLength = FrameLength.Is10ms,
                SampleRate = SampleRate.Is8kHz,
            };

            return vad.HasSpeech(audioFrame);
        }

        private static void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                var sample = rtpPacket.Payload;

                for (int index = 0; index < sample.Length; index++)
                {
                    if (rtpPacket.Header.PayloadType == (int)SDPWellKnownMediaFormatsEnum.PCMA)
                    {
                        short pcm = NAudio.Codecs.ALawDecoder.ALawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        byte[] aggregatedBytes = new byte[audioVadCheck.Length+pcmSample.Length];
                        Buffer.BlockCopy(audioVadCheck, 0, aggregatedBytes, 0, audioVadCheck.Length);
                        Buffer.BlockCopy(pcmSample, 0, aggregatedBytes, audioVadCheck.Length, pcmSample.Length);

                        audioVadCheck = aggregatedBytes;

                        _waveFile.Write(pcmSample, 0, 2);

                        vadCheckFrequency -= 1;

                        if (vadCheckFrequency == 0)
                        {
                            vadCheckFrequency = 80;

                            if (DoesFrameContainSpeech(audioVadCheck))
                            {
                                Console.WriteLine("There is speech.");
                            }
                            else
                            {
                                Console.WriteLine("There is silence!!!");
                            }
                            audioVadCheck = Array.Empty<byte>();
                        }

                    }
                    else
                    {
                        short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        byte[] aggregatedBytes = new byte[audioVadCheck.Length + pcmSample.Length];
                        Buffer.BlockCopy(audioVadCheck, 0, aggregatedBytes, 0, audioVadCheck.Length);
                        Buffer.BlockCopy(pcmSample, 0, aggregatedBytes, audioVadCheck.Length, pcmSample.Length);

                        audioVadCheck = aggregatedBytes;

                        _waveFile.Write(pcmSample, 0, 2);

                        vadCheckFrequency -= 1;

                        if (vadCheckFrequency == 0)
                        {
                            vadCheckFrequency = 80;

                            if (DoesFrameContainSpeech(audioVadCheck))
                            {
                                Console.WriteLine("There is speech.");
                            }
                            else
                            {
                                Console.WriteLine("There is silence!!!");
                            }

                            audioVadCheck=Array.Empty<byte>();
                        }
                    }
                }
            }
        }
    }
}