﻿using BeatSaberMultiplayer.Misc;
using SimpleJSON;
using SongLoaderPlugin;
using SongLoaderPlugin.OverrideClasses;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR;
using VRUI;

namespace BeatSaberMultiplayer
{
    class MultiplayerLobbyViewController : VRUINavigationController
    {
        public string selectedServerIP;
        public int selectedServerPort;

        private string beatSaverURL = "https://beatsaver.com";

        BSMultiplayerUI ui;

        Button _backButton;

        MultiplayerLeaderboardViewController _multiplayerLeaderboard;
        VotingSongListViewController _votingSongList;

        GameObject _loadingIndicator;
        private bool isLoading = false;
        public bool _loading { get { return isLoading; } set { isLoading = value; SetLoadingIndicator(isLoading); } }


        TextMeshProUGUI _timerText;
        TextMeshProUGUI _selectText;

        CustomLevel _selectedSong;
        int _selectedSongDifficulty;

        StandardLevelListTableCell _selectedSongCell;

        SongPreviewPlayer _songPreviewPlayer;

        ServerState _serverState = ServerState.Playing;
        
        float _sendRate = 1f / 20;
        float _sendTimer = 0;

        public PlayerInfo localPlayerInfo;
        string lastPlayerInfo;

        public List<PlayerInfo> _playerInfos = new List<PlayerInfo>();
        List<AvatarController> _avatars = new List<AvatarController>();

        List<CustomLevel> serverSongs = new List<CustomLevel>();

        protected override void DidActivate(bool firstActivation, ActivationType type)
        {
            

            ui = BSMultiplayerUI._instance;
            localPlayerInfo = new PlayerInfo(GetUserInfo.GetUserName(), GetUserInfo.GetUserID());

            if (_songPreviewPlayer == null)
            {
                ObjectProvider[] providers = Resources.FindObjectsOfTypeAll<ObjectProvider>().Where(x => x.name == "SongPreviewPlayerProvider").ToArray();

                if (providers.Length > 0)
                {
                    _songPreviewPlayer = providers[0].GetProvidedObject<SongPreviewPlayer>();
                }
            }

            if (_backButton == null)
            {
                _backButton = ui.CreateBackButton(rectTransform);

                _backButton.onClick.AddListener(delegate ()
                {
                    BSMultiplayerClient._instance.DataReceived -= DataReceived;
                    BSMultiplayerClient._instance.DisconnectFromServer();
                    _songPreviewPlayer.CrossfadeToDefault();
                    try
                    {
                        transform.parent.GetComponent<MultiplayerServerHubViewController>().UpdatePage();
                    } catch (Exception e)
                    {
                        Console.WriteLine($"ServerHub exception: {e}");
                    }
                    foreach (AvatarController avatar in _avatars)
                    {
                        Destroy(avatar.gameObject);
                    }
                    DismissModalViewController(null, false);
                    
                });
            }

            if (_timerText == null)
            {
                _timerText = ui.CreateText(rectTransform, "", new Vector2(0f, -5f));
                _timerText.fontSize = 8f;
                _timerText.alignment = TextAlignmentOptions.Center;
                _timerText.rectTransform.sizeDelta = new Vector2(30f, 6f);
            }

            if (_selectText == null)
            {
                _selectText = ui.CreateText(rectTransform, "", new Vector2(0f, -36f));
                _selectText.fontSize = 7f;
                _selectText.alignment = TextAlignmentOptions.Center;
                _selectText.rectTransform.sizeDelta = new Vector2(120f, 6f);
            }

            if (_loadingIndicator == null)
            {
                try
                {
                    _loadingIndicator = ui.CreateLoadingIndicator(rectTransform);
                    (_loadingIndicator.transform as RectTransform).anchorMin = new Vector2(0.5f, 0.5f);
                    (_loadingIndicator.transform as RectTransform).anchorMax = new Vector2(0.5f, 0.5f);
                    (_loadingIndicator.transform as RectTransform).anchoredPosition = new Vector2(0f, 0f);
                    _loadingIndicator.SetActive(true);

                }
                catch (Exception e)
                {
                    Console.WriteLine("EXCEPTION: " + e);
                }
            }

            if(_selectedSongCell == null)
            {
                _selectedSongCell = Instantiate(Resources.FindObjectsOfTypeAll<StandardLevelListTableCell>().First(x => x.name == "StandardLevelListTableCell"),rectTransform,false);
                
                (_selectedSongCell.transform as RectTransform).anchorMin = new Vector2(0.5f, 0.5f);
                (_selectedSongCell.transform as RectTransform).anchorMax = new Vector2(0.5f, 0.5f);
                (_selectedSongCell.transform as RectTransform).anchoredPosition = new Vector2(-25f, 0);

                _selectedSongCell.gameObject.SetActive(false);
            }
            else
            {
                _selectedSongCell.gameObject.SetActive(false);
            }

            if (BSMultiplayerClient._instance.ConnectToServer(selectedServerIP,selectedServerPort))
            {
                BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(new ClientCommand(ClientCommandType.GetAvailableSongs)));
                BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(new ClientCommand(ClientCommandType.GetServerState)));
                StartCoroutine(BSMultiplayerClient._instance.ReceiveFromServerCoroutine());
                BSMultiplayerClient._instance.DataReceived += DataReceived;
            }
            else
            {
                _loading = false;
                TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format("Can't connect to server!"), new Vector2(0f, -48f));
                _errorText.alignment = TextAlignmentOptions.Center;
                Destroy(_errorText.gameObject,5f);
            }

        }

        private void _votingSongList_selectedSong(CustomLevel selectedSong)
        {
            BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(
                new ClientCommand(ClientCommandType.VoteForSong,
                _voteForLevelId: selectedSong.levelID.Substring(0, 32))));
            PlayPreview(selectedSong);
        }

        private void CreateLeaderboard()
        {
            if (_multiplayerLeaderboard == null)
            {
                _multiplayerLeaderboard = ui.CreateViewController<MultiplayerLeaderboardViewController>();
                _multiplayerLeaderboard.rectTransform.anchorMin = new Vector2(0.3f, 0f);
                _multiplayerLeaderboard.rectTransform.anchorMax = new Vector2(0.7f, 1f);
            }
        }

        private void CreateSongList()
        {
            if (_votingSongList == null)
            {
                _votingSongList = BSMultiplayerUI._instance.CreateViewController<VotingSongListViewController>();
                _votingSongList.rectTransform.anchorMin = new Vector2(0.3f, 0f);
                _votingSongList.rectTransform.anchorMax = new Vector2(0.7f, 1f);

                _votingSongList.selectedSong -= _votingSongList_selectedSong;
                _votingSongList.selectedSong += _votingSongList_selectedSong;
            }
        }

        private void DataReceived(string[] _data)
        {
            if (_data != null && _data.Length > 0)
            {
                foreach (string data in _data)
                {
                    

                    ServerCommand command = null;

                    try
                    {
                        command = JsonUtility.FromJson<ServerCommand>(data);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Can't parse received JSON! Exception: " + e);
                        
                    }

                    try
                    {

                        if (command != null && BSMultiplayerClient._instance.IsConnected())
                        {
                            _serverState = command.serverState;
                            switch (command.commandType)
                            {
                                case ServerCommandType.UpdateRequired:
                                    {
                                        _selectText.text = "Plugin version mismatch:\nServer: "+command.version+"\nClient: "+BSMultiplayerClient.version;
                                        _timerText.text = "";
                                        BSMultiplayerClient._instance.DataReceived -= DataReceived;
                                        BSMultiplayerClient._instance.DisconnectFromServer();
                                        _loading = false;

                                    };break;
                                case ServerCommandType.SetServerState:
                                    {

                                        Console.WriteLine($"Server state is {command.serverState}");

                                        if (_serverState == ServerState.Playing)
                                        {
                                            _loading = false;
                                            TimeSpan timeRemaining = TimeSpan.FromSeconds(command.selectedSongDuration - command.selectedSongPlayTime);

                                            _timerText.text = timeRemaining.Minutes.ToString("00") + ":" + timeRemaining.Seconds.ToString("00");

                                            _selectedSong = SongLoader.CustomLevels.First(x => x.levelID.Substring(0, 32) == command.selectedLevelID.Substring(0, 32));

                                            _selectedSongCell.gameObject.SetActive(true);
                                            (_selectedSongCell.transform as RectTransform).anchoredPosition = new Vector2(18f, 39f);
                                            _selectedSongCell.songName = _selectedSong.songName + "\n<size=80%>" + _selectedSong.songSubName + "</size>";
                                            _selectedSongCell.author = _selectedSong.songAuthorName;

                                            _selectedSongCell.coverImage = _selectedSong.coverImage;
                                            
                                            SongLoader.Instance.LoadAudioClipForLevel(_selectedSong, PlayPreview);

                                            CreateLeaderboard();
                                            PushViewController(_multiplayerLeaderboard, true);

                                        }else if(_serverState == ServerState.Preparing)
                                        {
                                            if (_multiplayerLeaderboard != null)
                                            {
                                                _viewControllers.Remove(_multiplayerLeaderboard);
                                                Destroy(_multiplayerLeaderboard.gameObject);
                                            }
                                            UpdateSelectedSong(command);
                                        }
                                        else if(_serverState == ServerState.Voting)
                                        {
                                            if (_multiplayerLeaderboard != null)
                                            {
                                                _viewControllers.Remove(_multiplayerLeaderboard);
                                                Destroy(_multiplayerLeaderboard.gameObject);
                                            }
                                            CreateSongList();
                                            PushViewController(_votingSongList, true);
                                            _votingSongList.SetSongs(serverSongs.ToList());
                                        }
                                        
                                    }; break;
                                case ServerCommandType.SetLobbyTimer:
                                    {
                                        _loading = false;
                                        Console.WriteLine("Set timer text to " + command.lobbyTimer);
                                        _timerText.text = command.lobbyTimer.ToString();
                                        if (_multiplayerLeaderboard != null)
                                        {
                                            _viewControllers.Remove(_multiplayerLeaderboard);
                                            Destroy(_multiplayerLeaderboard.gameObject);
                                            if (_serverState == ServerState.Voting)
                                            {
                                                _songPreviewPlayer.CrossfadeToDefault();
                                                _selectedSongCell.gameObject.SetActive(false);
                                            }
                                        }
                                    }; break;
                                case ServerCommandType.SetSelectedSong:
                                    {
                                        if (command.serverState == ServerState.Preparing)
                                        {
                                            if (_votingSongList != null)
                                            {
                                                _viewControllers.Remove(_votingSongList);
                                                Destroy(_votingSongList.gameObject);
                                            }
                                            Console.WriteLine("Next song level id is " + command.selectedLevelID);
                                            UpdateSelectedSong(command);
                                        }

                                    }; break;
                                case ServerCommandType.StartSelectedSongLevel:
                                    {
                                        Console.WriteLine("Removing avatars from lobby");

                                        foreach (AvatarController avatar in _avatars)
                                        {
                                            Destroy(avatar.gameObject);
                                        }

                                        _avatars.Clear();

                                        _selectedSong = SongLoader.CustomLevels.First(x => x.levelID.Substring(0, 32) == command.selectedLevelID.Substring(0, 32));
                                        _selectedSongDifficulty = command.selectedSongDifficlty;

                                        Console.WriteLine("Starting selected song! Song: " + _selectedSong.songName + ", Diff: " + ((LevelDifficulty)_selectedSongDifficulty).ToString());
                                        
                                        BSMultiplayerClient._instance.DataReceived -= DataReceived;
                                        GameplayOptions gameplayOptions = new GameplayOptions();
                                        gameplayOptions.noEnergy = command.noFailMode;

                                        if (BSMultiplayerClient._instance._mainGameSceneSetupData != null)
                                        {
                                            BSMultiplayerClient._instance._mainGameSceneSetupData.Init(_selectedSong.GetDifficultyLevel((LevelDifficulty)_selectedSongDifficulty), gameplayOptions, GameplayMode.SoloStandard, 0f);
                                            BSMultiplayerClient._instance.BindToFinishEvent();
                                            BSMultiplayerClient._instance._mainGameSceneSetupData.TransitionToScene(0.7f);
                                            _selectedSong = null;
                                            return;
                                        }
                                        else
                                        {
                                            Console.WriteLine("SceneSetupData is null!");
                                        }
                                        
                                    }; break;
                                case ServerCommandType.DownloadSongs: {
                                        if (!AllSongsDownloaded(command.songsToDownload))
                                        {
                                            _timerText.text = "";
                                            if (_votingSongList != null)
                                            {
                                                _viewControllers.Remove(_votingSongList);
                                                Destroy(_votingSongList.gameObject);
                                            }
                                            if (_multiplayerLeaderboard != null)
                                            {
                                                _viewControllers.Remove(_multiplayerLeaderboard);
                                                Destroy(_multiplayerLeaderboard.gameObject);
                                            }
                                            _songPreviewPlayer.CrossfadeToDefault();
                                            _selectedSongCell.gameObject.SetActive(false);
                                            StartCoroutine(DownloadSongs(command.songsToDownload));
                                            BSMultiplayerClient._instance.DataReceived -= DataReceived;
                                            BSMultiplayerClient._instance.DisconnectFromServer();
                                        }
                                        else
                                        {
                                            serverSongs.Clear();

                                            foreach(string song in command.songsToDownload)
                                            {
                                                serverSongs.Add(SongLoader.CustomLevels.FirstOrDefault(x => x.levelID.Substring(0, 32) == song.Substring(0, 32)));
                                            }
                                        }
                                    };break;
                                case ServerCommandType.SetPlayerInfos: {

                                        if (command.serverState == ServerState.Playing)
                                        {
                                            if (_multiplayerLeaderboard != null)
                                            {

                                                TimeSpan timeRemaining = TimeSpan.FromSeconds(command.selectedSongDuration - command.selectedSongPlayTime);

                                                if (timeRemaining.Ticks >= 0)
                                                {
                                                    _timerText.text = timeRemaining.Minutes.ToString("00") + ":" + timeRemaining.Seconds.ToString("00");
                                                }
                                                else
                                                {
                                                    _timerText.text = "RESULTS "+ Math.Abs(timeRemaining.Seconds+15).ToString("00");
                                                }
                                                try
                                                {
                                                    _multiplayerLeaderboard.SetLeaderboard(command.playerInfos.Select(x => JsonUtility.FromJson<PlayerInfo>(x)).ToArray());
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine("Leaderboard exception: " + e);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (Config.Instance.ShowAvatarsInLobby)
                                            {
                                                _playerInfos.Clear();
                                                foreach (string playerStr in command.playerInfos)
                                                {
                                                    PlayerInfo player = JsonUtility.FromJson<PlayerInfo>(playerStr);
                                                    if (!String.IsNullOrEmpty(player.playerAvatar))
                                                    {
                                                        byte[] avatar = Convert.FromBase64String(player.playerAvatar);

                                                        player.rightHandPos = Serialization.ToVector3(avatar.Take(12).ToArray());
                                                        player.leftHandPos = Serialization.ToVector3(avatar.Skip(12).Take(12).ToArray());
                                                        player.headPos = Serialization.ToVector3(avatar.Skip(24).Take(12).ToArray());

                                                        player.rightHandRot = Serialization.ToQuaternion(avatar.Skip(36).Take(16).ToArray());
                                                        player.leftHandRot = Serialization.ToQuaternion(avatar.Skip(52).Take(16).ToArray());
                                                        player.headRot = Serialization.ToQuaternion(avatar.Skip(68).Take(16).ToArray());

                                                    }
                                                    _playerInfos.Add(player);
                                                }

                                                try
                                                {
                                                    if (_avatars.Count > _playerInfos.Count)
                                                    {
                                                        List<AvatarController> avatarsToRemove = new List<AvatarController>();
                                                        for (int i = _playerInfos.Count; i < _avatars.Count; i++)
                                                        {
                                                            avatarsToRemove.Add(_avatars[i]);
                                                        }
                                                        foreach (AvatarController avatar in avatarsToRemove)
                                                        {
                                                            _avatars.Remove(avatar);
                                                            Destroy(avatar.gameObject);
                                                        }

                                                    }
                                                    else if (_avatars.Count < _playerInfos.Count)
                                                    {
                                                        for (int i = 0; i < (_playerInfos.Count - _avatars.Count); i++)
                                                        {
                                                            _avatars.Add(new GameObject("Avatar").AddComponent<AvatarController>());

                                                        }
                                                    }

                                                    for (int i = 0; i < _playerInfos.Count; i++)
                                                    {
                                                        _avatars[i].SetPlayerInfo(_playerInfos[i], 0f, localPlayerInfo.Equals(_playerInfos[i]));
                                                    }
                                                }
                                                catch (Exception e)
                                                {
                                                    Console.WriteLine($"AVATARS EXCEPTION: {e}");
                                                }
                                            }
                                        }

                                    };break;
                                case ServerCommandType.Kicked: {
                                        _selectText.text = $"You were kicked{(string.IsNullOrEmpty(command.kickReason) ? "" : $"\nReason: {command.kickReason}")}";
                                        _timerText.text = "";
                                        if (_multiplayerLeaderboard != null && _viewControllers.IndexOf(_multiplayerLeaderboard) >= 0)
                                        {
                                            _viewControllers.Remove(_multiplayerLeaderboard);
                                            _multiplayerLeaderboard.gameObject.SetActive(false);
                                            _songPreviewPlayer.CrossfadeToDefault();
                                            _selectedSongCell.gameObject.SetActive(false);
                                        }
                                        BSMultiplayerClient._instance.DataReceived -= DataReceived;
                                        BSMultiplayerClient._instance.DisconnectFromServer();
                                        _loading = false;
                                    }; break;


                            }
                        }
                        else
                        {
                            Console.WriteLine("Server Command is null!");
                        }
                    }catch(Exception e)
                    {
                        Console.WriteLine("Exception (parse switch): "+e);
                    }
                    

                }
            }
                

            StartCoroutine(BSMultiplayerClient._instance.ReceiveFromServerCoroutine());
        }

        void UpdateSelectedSong(ServerCommand command)
        {
            if (string.IsNullOrEmpty(command.selectedLevelID))
            {
                return;
            }
            if(_selectedSong != null && _selectedSong.levelID.Substring(0, 32) == command.selectedLevelID.Substring(0, 32))
            {
                return;
            }
            Console.WriteLine("Set selected song " + command.selectedLevelID);

            _loading = false;

            try
            {
                _selectedSong = SongLoader.CustomLevels.First(x => x.levelID.Substring(0, 32) == command.selectedLevelID.Substring(0, 32));
                
                SongLoader.Instance.LoadAudioClipForLevel(_selectedSong, PlayPreview);

                _selectText.gameObject.SetActive(true);
                _selectText.text = "Next song:";

                _selectedSongCell.gameObject.SetActive(true);
                (_selectedSongCell.transform as RectTransform).anchoredPosition = new Vector2(-25f, 0);
                _selectedSongCell.songName = _selectedSong.songName + "\n<size=80%>" + _selectedSong.songSubName + "</size>";
                _selectedSongCell.author = _selectedSong.songAuthorName;

                _selectedSongCell.coverImage = _selectedSong.coverImage;
            }
            catch (Exception e)
            {
                Console.WriteLine("EXCEPTION: " + e);
            }
        }

        void PlayPreview(CustomLevel _songData)
        {
            Console.WriteLine("Playing preview for " + _songData.songName);
            if (_songData.audioClip != null)
            {
                if (_songPreviewPlayer != null && _songData != null)
                {
                    try
                    {
                        _songPreviewPlayer.CrossfadeTo(_songData.audioClip, _songData.previewStartTime, 900f, 1f);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Can't play preview! Exception: " + e);
                    }
                }
            }
        }

        IEnumerator DownloadSongByLevelId(string levelId)
        {
            Console.WriteLine("Downloading "+ levelId.Substring(0, levelId.IndexOf('∎')));
            
            _selectText.text = "Connecting to BeatSaver...";
            
            UnityWebRequest wwwId = UnityWebRequest.Get($"{beatSaverURL}/api/songs/search/hash/" + levelId.Substring(0,levelId.IndexOf('∎')));
            wwwId.timeout = 10;

            yield return wwwId.SendWebRequest();
            

            if (wwwId.isNetworkError || wwwId.isHttpError)
            {
                Console.WriteLine(wwwId.error);

                _selectText.text = "";

                TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format(wwwId.error), new Vector2(0f, -48f));
                _errorText.alignment = TextAlignmentOptions.Center;
                Destroy(_errorText.gameObject, 2f);
            }
            else
            {
                Console.WriteLine("Received response from BeatSaver");

                JSONNode node = JSON.Parse(wwwId.downloadHandler.text);
                
                if(node["songs"].Count == 0)
                {
                    _selectText.text = $"Song {levelId.Split('∎')[1]} doesn't exist!";
                    yield break;
                }

                Song _tempSong = Song.FromSearchNode(node["songs"][0]);

                UnityWebRequest wwwDl = UnityWebRequest.Get(_tempSong.downloadUrl);
                
                bool timeout = false;
                float time = 0f;

                UnityWebRequestAsyncOperation asyncRequest = wwwDl.SendWebRequest();

                while (!asyncRequest.isDone || asyncRequest.progress < 1f)
                {
                    yield return null;

                    time += Time.deltaTime;

                    if (time >= 15f && asyncRequest.progress == 0f)
                    {
                        wwwDl.Abort();
                        timeout = true;
                    }

                    _selectText.text = "Downloading " + _tempSong.beatname + " - "+Math.Round(asyncRequest.progress*100)+"%";
                }

                if (wwwId.isNetworkError || wwwId.isHttpError || timeout)
                {
                    if (timeout)
                    {
                        Console.WriteLine("Request timeout");
                        TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format("Request timeout"), new Vector2(0f, -48f));
                        _errorText.alignment = TextAlignmentOptions.Center;
                        Destroy(_errorText.gameObject, 2f);
                    }
                    else
                    {
                        Console.WriteLine(wwwId.error);
                        TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format(wwwId.error), new Vector2(0f, -48f));
                        _errorText.alignment = TextAlignmentOptions.Center;
                        Destroy(_errorText.gameObject, 2f);
                    }
                }
                else
                {
                    string docPath = "";
                    string customSongsPath = "";
                    string zipPath = "";
                    try
                    {
                        byte[] data = wwwDl.downloadHandler.data;

                        docPath = Application.dataPath;
                        docPath = docPath.Substring(0, docPath.Length - 5);
                        docPath = docPath.Substring(0, docPath.LastIndexOf("/"));
                        customSongsPath = docPath + "/CustomSongs/" + _tempSong.id + "/";
                        zipPath = customSongsPath + _tempSong.id + ".zip";
                        if (!Directory.Exists(customSongsPath))
                        {
                            Directory.CreateDirectory(customSongsPath);
                        }
                        File.WriteAllBytes(zipPath, data);
                        Console.WriteLine("Downloaded zip file!");

                        Console.WriteLine("Extracting...");

                        try
                        {
                            ZipFile.ExtractToDirectory(zipPath, customSongsPath);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Can't extract ZIP! Exception: {e}");
                        }


                        File.Delete(zipPath);

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("EXCEPTION: " + e);
                        yield break;
                    }
                }
            }
        }

        IEnumerator DownloadSongs(string[] _levelIds)
        {
            _timerText.text = "";
            bool needToRefresh = false;

            foreach(string levelId in _levelIds)
            {
                if(!SongLoader.CustomLevels.Any(x => x.levelID.Substring(0,32) == levelId.Substring(0, 32)))
                {
                    needToRefresh = true;
                    Console.WriteLine("Need to download "+levelId);

                    yield return DownloadSongByLevelId(levelId);
                }
            }
            _selectText.text = "";
            
            yield return null;

            if (needToRefresh)
            {
                SongLoader.SongsLoadedEvent += SongsLoaded;
                SongLoader.Instance.RefreshSongs(false);
            }
        }

        private void SongsLoaded(SongLoader sender, List<CustomLevel> levels)
        {
            if (BSMultiplayerClient._instance.ConnectToServer(selectedServerIP, selectedServerPort))
            {
                BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(new ClientCommand(ClientCommandType.GetAvailableSongs)));
                BSMultiplayerClient._instance.SendString(JsonUtility.ToJson(new ClientCommand(ClientCommandType.GetServerState)));
                BSMultiplayerClient._instance.DataReceived += DataReceived;
                StartCoroutine(BSMultiplayerClient._instance.ReceiveFromServerCoroutine());
            }
            else
            {
                _loading = false;
                TextMeshProUGUI _errorText = ui.CreateText(rectTransform, String.Format("Can't connect to server!"), new Vector2(0f, -48f));
                _errorText.alignment = TextAlignmentOptions.Center;
                Destroy(_errorText.gameObject, 5f);
            }
        }


        bool AllSongsDownloaded(string[] levelIds)
        {
            bool allDownloaded = true;

            foreach (string levelId in levelIds)
            {
                if (!SongLoader.CustomLevels.Select(x => x.levelID.Substring(0, 32)).Contains(levelId.Substring(0, 32)))
                {
                    allDownloaded = false;
                }

            }

            return allDownloaded;
        }


        void Update()
        {
            if (_serverState == ServerState.Voting || _serverState == ServerState.Preparing)
            {
                _sendTimer += Time.deltaTime;
                if (_sendTimer > _sendRate)
                {
                    _sendTimer = 0;
                    localPlayerInfo.playerAvatar = Convert.ToBase64String(
                        Serialization.Combine(
                            Serialization.ToBytes(InputTracking.GetLocalPosition(XRNode.RightHand)),
                            Serialization.ToBytes(InputTracking.GetLocalPosition(XRNode.LeftHand)),
                            Serialization.ToBytes(InputTracking.GetLocalPosition(XRNode.Head)),
                            Serialization.ToBytes(InputTracking.GetLocalRotation(XRNode.RightHand)),
                            Serialization.ToBytes(InputTracking.GetLocalRotation(XRNode.LeftHand)),
                            Serialization.ToBytes(InputTracking.GetLocalRotation(XRNode.Head))
                       ));

                    string playerInfoString = JsonUtility.ToJson(new ClientCommand(ClientCommandType.SetPlayerInfo, JsonUtility.ToJson(localPlayerInfo)));

                    if (playerInfoString != lastPlayerInfo)
                    {
                        BSMultiplayerClient._instance.SendString(playerInfoString);
                        lastPlayerInfo = playerInfoString;
                    }

                }
            }
        }

        void SetLoadingIndicator(bool loading)
        {
            if (_loadingIndicator)
            {
                _loadingIndicator.SetActive(loading);
            }
        }

    }
}
