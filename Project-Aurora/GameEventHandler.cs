﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;
using Aurora.Profiles;
using Aurora.Profiles.Dota_2;
using Aurora.Profiles.Desktop;
using Aurora.Profiles.CSGO;
using System.Runtime.InteropServices;
using Aurora.Profiles.GTA5;
using Aurora.Profiles.Logitech_Wrapper;
using Aurora.Profiles.Generic_Application;
using Aurora.Utils;
using Aurora.Profiles.RocketLeague;
using Aurora.Profiles.Payday_2;

namespace Aurora
{
    public enum PreviewType
    {
        None,
        Desktop,
        Predefined,
        GenericApplication
    }

    public class GameEventHandler
    {
        public struct tagLASTINPUTINFO
        {
            public uint cbSize;
            public Int32 dwTime;
        }

        [DllImport("user32.dll")]
        public static extern Boolean GetLastInputInfo(ref tagLASTINPUTINFO plii);


        private Event_Desktop desktop_e = new Event_Desktop();
        private Event_Idle idle_e = new Event_Idle();
        //private GameEvent_Logitech_Wrapper logiwr_ge = new GameEvent_Logitech_Wrapper();
        private Dictionary<string, GameEvent> profiles = new Dictionary<string, GameEvent>(); //Process name, GameEvent

        private List<TimedListObject> overlays = new List<TimedListObject>();

        private bool isForced = false;

        private Timer update_timer;

        private string process_path = "";
        private long currentTick = 0L;
        private long nextProcessNameUpdate = 0L;

        private PreviewType preview_mode = PreviewType.Desktop;
        private string preview_mode_profile_key = "";

        public GameEventHandler()
        {
            //Include all pre-made profiles
            profiles.Add("dota2.exe", new GameEvent_Dota2());
            profiles.Add("csgo.exe", new GameEvent_CSGO());
            profiles.Add("gta5.exe", new GameEvent_GTA5());
            profiles.Add("rocketleague.exe", new GameEvent_RocketLeague());
            profiles.Add("payday2_win32_release.exe", new GameEvent_PD2());

        }

        ~GameEventHandler()
        {
            Destroy();
        }

        public bool Init()
        {
            bool devices_inited = Global.dev_manager.AnyInitialized();

            if (devices_inited)
            {
                update_timer = new Timer(10);
                update_timer.Elapsed += new ElapsedEventHandler(update_timer_Tick);
                update_timer.Interval = 10; // in miliseconds
                update_timer.Start();
            }

            if (!devices_inited)
                Global.logger.LogLine("No devices initialized.", Logging_Level.Warning);

            return devices_inited;
        }

        public void Destroy()
        {
            update_timer?.Stop();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private string GetActiveWindowsProcessname()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                uint procId = 0;
                GetWindowThreadProcessId(hWnd, out procId);
                var proc = Process.GetProcessById((int)procId);
                return proc.MainModule.FileName;
            }
            catch (Exception exc)
            {
                //Console.WriteLine(exc);
                return "";
            }
        }

        private void update_timer_Tick(object sender, EventArgs e)
        {
            if(currentTick >= nextProcessNameUpdate)
            {
                process_path = GetActiveWindowsProcessname().ToLowerInvariant();
                nextProcessNameUpdate = currentTick + 1000L;
            }

            string process_name = System.IO.Path.GetFileName(process_path);

            if (Global.Configuration.excluded_programs.Contains(process_name))
            {
                return;
            }

            EffectsEngine.EffectFrame newframe = new EffectsEngine.EffectFrame();

            tagLASTINPUTINFO LastInput = new tagLASTINPUTINFO();
            Int32 IdleTime;
            LastInput.cbSize = (uint)Marshal.SizeOf(LastInput);
            LastInput.dwTime = 0;

            if (GetLastInputInfo(ref LastInput))
            {
                IdleTime = System.Environment.TickCount - LastInput.dwTime;

                if (IdleTime >= Global.Configuration.desktop_settings.idle_delay * 60 * 1000)
                {
                    if (!(Global.Configuration.desktop_settings.time_based_dimming_enabled &&
                    Utils.Time.IsCurrentTimeBetween(Global.Configuration.desktop_settings.time_based_dimming_start_hour, Global.Configuration.desktop_settings.time_based_dimming_start_minute, Global.Configuration.desktop_settings.time_based_dimming_end_hour, Global.Configuration.desktop_settings.time_based_dimming_end_minute))
                    )
                    {
                        idle_e.UpdateLights(newframe);
                    }
                }
            }

            if (Global.Configuration.additional_profiles.ContainsKey(process_name) && Global.Configuration.additional_profiles[process_name].isEnabled)
            {
                if (profiles.ContainsKey(process_name))
                {
                    profiles[process_name].UpdateLights(newframe);
                }
                else
                {
                    Event_GenericApplication app_event = new Event_GenericApplication(process_name);
                    app_event.UpdateLights(newframe);
                    profiles.Add(process_name, app_event);
                }

            }
            else if (preview_mode == PreviewType.GenericApplication && Global.Configuration.additional_profiles.ContainsKey(preview_mode_profile_key) && Global.Configuration.additional_profiles[preview_mode_profile_key].isEnabled)
            {
                if (profiles.ContainsKey(preview_mode_profile_key))
                    profiles[preview_mode_profile_key].UpdateLights(newframe);
                else
                {
                    Event_GenericApplication app_event = new Event_GenericApplication(preview_mode_profile_key);
                    app_event.UpdateLights(newframe);
                    profiles.Add(preview_mode_profile_key, app_event);
                }
            }
            else if (preview_mode == PreviewType.Predefined && profiles.ContainsKey(preview_mode_profile_key) && profiles[preview_mode_profile_key].IsEnabled())
            {
                profiles[preview_mode_profile_key].UpdateLights(newframe);
            }
            else if(profiles.ContainsKey(process_name) && profiles[process_name].IsEnabled())
            {
                update_timer.Interval = 10; // in miliseconds

                if (!(Global.Configuration.desktop_settings.time_based_dimming_enabled && Global.Configuration.desktop_settings.time_based_dimming_affect_games &&
                    Utils.Time.IsCurrentTimeBetween(Global.Configuration.desktop_settings.time_based_dimming_start_hour, Global.Configuration.desktop_settings.time_based_dimming_start_minute, Global.Configuration.desktop_settings.time_based_dimming_end_hour, Global.Configuration.desktop_settings.time_based_dimming_end_minute))
                    )
                {
                    profiles[process_name].UpdateLights(newframe);
                }
            }
            else
            {
                update_timer.Interval = 1000.0D / 30; //50 in miliseconds
                if (!(Global.Configuration.desktop_settings.time_based_dimming_enabled &&
                    Utils.Time.IsCurrentTimeBetween(Global.Configuration.desktop_settings.time_based_dimming_start_hour, Global.Configuration.desktop_settings.time_based_dimming_start_minute, Global.Configuration.desktop_settings.time_based_dimming_end_hour, Global.Configuration.desktop_settings.time_based_dimming_end_minute))
                    )
                {
                    desktop_e.UpdateLights(newframe);
                }

            }

            //Add overlays
            TimedListObject[] overlay_events = overlays.ToArray();
            foreach (TimedListObject evnt in overlay_events)
            {
                (evnt.item as GameEvent).UpdateLights(newframe);
            }

            Global.effengine.PushFrame(newframe);

            currentTick += (long)update_timer.Interval;
        }

        public void GameStateUpdate(GameState gs)
        {
            //Debug.WriteLine("Received gs!");

            //Global.logger.LogLine(gs.ToString(), Logging_Level.None, false);

            if (currentTick >= nextProcessNameUpdate)
            {
                process_path = GetActiveWindowsProcessname().ToLowerInvariant();
                nextProcessNameUpdate = currentTick + 1000L;
            }

            string process_name = System.IO.Path.GetFileName(process_path);

            if (Global.Configuration.excluded_programs.Contains(process_name))
            {
                return;
            }

            EffectsEngine.EffectFrame newframe = new EffectsEngine.EffectFrame();

            try
            {
                bool resolved_game = false;

                switch (Newtonsoft.Json.Linq.JObject.Parse(gs.GetNode("provider")).GetValue("appid").ToString())
                {
                    case "570":
                        if (process_name.EndsWith("dota2.exe") && profiles.ContainsKey(process_name) && profiles[process_name].IsEnabled())
                        {
                            profiles[process_name].UpdateLights(newframe, new Profiles.Dota_2.GSI.GameState_Dota2(gs));
                            resolved_game = true;
                        }
                        break;
                    case "730":
                        if (process_name.EndsWith("csgo.exe") && profiles.ContainsKey(process_name) && profiles[process_name].IsEnabled())
                        {
                            profiles[process_name].UpdateLights(newframe, new Profiles.CSGO.GSI.GameState_CSGO(gs));
                            resolved_game = true;
                        }
                        break;
                    case "218620":
                        if (process_name.EndsWith("payday2_win32_release.exe") && profiles.ContainsKey(process_name) && profiles[process_name].IsEnabled())
                        {
                            profiles[process_name].UpdateLights(newframe, new Profiles.Payday_2.GSI.GameState_PD2(gs));
                            resolved_game = true;
                        }
                        break;
                    case "0":
                        if (process_name.EndsWith("gta5.exe") && Newtonsoft.Json.Linq.JObject.Parse(gs.GetNode("provider")).GetValue("name").ToString().ToLowerInvariant().Equals("gta5.exe") && profiles.ContainsKey(process_name) && profiles[process_name].IsEnabled())
                        {
                            profiles[process_name].UpdateLights(newframe, gs as Profiles.GTA5.GSI.GameState_GTA5);
                            resolved_game = true;
                        }
                        else
                        {
                            if (gs is GameState_Wrapper && Global.Configuration.allow_all_logitech_bitmaps)
                            {
                                string gs_process_name = Newtonsoft.Json.Linq.JObject.Parse(gs.GetNode("provider")).GetValue("name").ToString().ToLowerInvariant();

                                if(!profiles.ContainsKey(gs_process_name))
                                    profiles.Add(gs_process_name, new GameEvent_Logitech_Wrapper());

                                if(process_name.EndsWith(gs_process_name))
                                {
                                    profiles[gs_process_name].UpdateLights(newframe, gs as GameState_Wrapper);
                                    resolved_game = true;
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
                tagLASTINPUTINFO LastInput = new tagLASTINPUTINFO();
                Int32 IdleTime;
                LastInput.cbSize = (uint)Marshal.SizeOf(LastInput);
                LastInput.dwTime = 0;

                if (GetLastInputInfo(ref LastInput) && resolved_game)
                {
                    IdleTime = System.Environment.TickCount - LastInput.dwTime;

                    if (IdleTime >= Global.Configuration.desktop_settings.idle_delay * 60 * 1000)
                    {
                        if (!(Global.Configuration.desktop_settings.time_based_dimming_enabled && Global.Configuration.desktop_settings.time_based_dimming_affect_games &&
                    Utils.Time.IsCurrentTimeBetween(Global.Configuration.desktop_settings.time_based_dimming_start_hour, Global.Configuration.desktop_settings.time_based_dimming_start_minute, Global.Configuration.desktop_settings.time_based_dimming_end_hour, Global.Configuration.desktop_settings.time_based_dimming_end_minute))
                    )
                        {
                            idle_e.UpdateLights(newframe);
                        }
                    }
                }

                if (!(Global.Configuration.desktop_settings.time_based_dimming_enabled && Global.Configuration.desktop_settings.time_based_dimming_affect_games &&
                    Utils.Time.IsCurrentTimeBetween(Global.Configuration.desktop_settings.time_based_dimming_start_hour, Global.Configuration.desktop_settings.time_based_dimming_start_minute, Global.Configuration.desktop_settings.time_based_dimming_end_hour, Global.Configuration.desktop_settings.time_based_dimming_end_minute))
                    )
                {
                    if (resolved_game)
                    {
                        //Add overlays
                        TimedListObject[] overlay_events = overlays.ToArray();
                        foreach (TimedListObject evnt in overlay_events)
                        {
                            (evnt.item as GameEvent).UpdateLights(newframe);
                        }

                        Global.effengine.PushFrame(newframe);
                    }

                }
            }
            catch (Exception e)
            {
                Global.logger.LogLine("Exception during GameStateUpdate(), error: " + e, Logging_Level.Warning);
            }
        }

        public void SetForcedUpdate(bool forced)
        {
            this.isForced = forced;
        }

        public void SetPreview(PreviewType preview, string profile_key = "")
        {
            this.preview_mode = preview;
            this.preview_mode_profile_key = profile_key;
        }

        public PreviewType GetPreview()
        {
            return this.preview_mode;
        }

        public string GetPreviewProfileKey()
        {
            return this.preview_mode_profile_key;
        }

        public void AddOverlayForDuration(GameEvent overlay_event, int duration, bool isUnique = true)
        {
            if (isUnique)
            {
                TimedListObject[] overlays_array = overlays.ToArray();
                bool isFound = false;

                foreach (TimedListObject obj in overlays_array)
                {
                    if (obj.item.GetType() == overlay_event.GetType())
                    {
                        isFound = true;
                        obj.AdjustDuration(duration);
                        break;
                    }
                }
                
                if(!isFound)
                {
                    overlays.Add(new TimedListObject(overlay_event, duration, overlays));
                }
            }
            else
            {
                overlays.Add(new TimedListObject(overlay_event, duration, overlays));
            }
        }
    }
}
