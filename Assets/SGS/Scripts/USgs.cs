using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpeechGenerationSystem;
using UnityEngine;

namespace UnityText2Speech {
    public class USgs : MonoBehaviour {
        public string espeakDirPath = "SGS/espeak-ng-data/";
        public string voiceModelFilePath = "SGS/voices/en-us-kathleen-low.onnx";
        public string voiceConfigFilePath = "SGS/voices/en-us-kathleen-low.onnx.json";
        public ulong voiceId = 0;

        public AudioSource audioPlayer = null;
        // Start is called before the first frame update

        public void ReceiveTextToSpeech( string text ) {
            const int LETTER_LIMIT = 400;
            const string WORDS_DELIMITER = " ";
            var fragmentsQueue = new Queue<string>( _sentenceSplitPattern.Split( text ) );
            while ( fragmentsQueue.Count > 0 ) {
                var fragment = fragmentsQueue.Dequeue( );
                if ( fragmentsQueue.Count > 0 ) {
                    fragment += fragmentsQueue.Dequeue( );
                }
                if ( fragment.Length > LETTER_LIMIT ) {
                    var wordsQueue = new Queue<string>( fragment.Split( WORDS_DELIMITER ) );
                    var sb = new StringBuilder( );
                    while ( wordsQueue.Count > 0 ) {
                        if ( sb.Length < LETTER_LIMIT ) {
                            var word = wordsQueue.Dequeue( ) + WORDS_DELIMITER;
                            sb.Append( word );
                        }
                        else {
                            _textQueue.Enqueue( sb.ToString( ) );
                            sb.Clear( );
                        }
                    }
                    if ( sb.Length > 0 ) {
                        _textQueue.Enqueue( sb.ToString( ) );
                    }
                }
                else {
                    _textQueue.Enqueue( fragment );
                }
            }
        }

        private void Start( ) {
            var espeakDataFPath = $"{Application.streamingAssetsPath}/{espeakDirPath}";
            var voiceModelFPath = $"{Application.streamingAssetsPath}/{voiceModelFilePath}";
            var voiceConfigFPath = $"{Application.streamingAssetsPath}/{voiceConfigFilePath}";
            var initResult = _sgsApi.Init( espeakDataFPath, voiceModelFPath, voiceConfigFPath, voiceId );
            switch ( initResult ) {
                case InitResult.Success:
                    _inited = true;
                    _processingTask = Task.Run( ProcessingAsync );
                    break;
                case InitResult.ESpeakNotFound:
                    Debug.LogErrorFormat( "USgs. Directory {0} not found!", espeakDataFPath );
                    break;
                case InitResult.VoiceModelNotFound:
                    Debug.LogErrorFormat( "USgs. File {0} not found!", voiceModelFPath );
                    break;
                case InitResult.VoiceConfigNotFound:
                    Debug.LogErrorFormat( "USgs. File {0} not found!", voiceConfigFPath );
                    break;
                case InitResult.VoiceIsNull:
                    Debug.LogErrorFormat( "USgs. Voice is null!" );
                    break;
            }
        }

        private async Task ProcessingAsync( ) {
            const int DELAY_MS = 100;
            while ( _inited ) {
                if ( _textQueue.TryDequeue( out string text ) ) {
                    if ( _sgsApi.GenerateSpeech( text ) ) {
                        _pcmDataQueue.Enqueue( _sgsApi.PcmData );
                    }
                    else {
                        Debug.LogWarningFormat( "USgs. Failed to generate audio for the text {0}", text );
                    }
                }
                else {
                    await Task.Delay( DELAY_MS );
                }
            }
        }

        private SgsApi _sgsApi = new SgsApi( );
        private bool _inited = false;
        private readonly ConcurrentQueue<string> _textQueue = new ConcurrentQueue<string>( );
        private readonly ConcurrentQueue<PcmData> _pcmDataQueue = new ConcurrentQueue<PcmData>( );
        private Regex _sentenceSplitPattern = new Regex( @"(\.|\!|\?|\n)" );
        private Task _processingTask = null;
        // Update is called once per frame
        private void FixedUpdate( ) {
            if ( !audioPlayer.isPlaying ) {
                if ( _pcmDataQueue.TryDequeue( out PcmData pcmData ) ) {
                    var audioClip = AudioClip.Create( "default", pcmData.Length, pcmData.Channels, pcmData.SampleRate, false );
                    audioClip.SetData( pcmData.Value, 0 );
                    audioPlayer.clip = audioClip;
                    audioPlayer.Play( );
                }
            }
        }

        private void OnDestroy( ) {
            _inited = false;
            if ( audioPlayer.isPlaying ) {
                audioPlayer.Stop( );
            }
            if ( _processingTask != null ) {
                _processingTask.Wait( );
                _processingTask = null;
            }
            if ( _sgsApi != null ) {
                _sgsApi.Dispose( );
                _sgsApi = null;
            }
        }
    }

}