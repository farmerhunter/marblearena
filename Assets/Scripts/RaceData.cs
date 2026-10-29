using System;
using UnityEngine;

namespace MarbleArena
{
    [Serializable]
    public class RacerDefinition
    {
        public string id, name_zh, name_en, description, primary_color, secondary_color, core_style, family, team_id, strategy, marble_pattern;
        public Color Color => Visuals.Parse(primary_color);
        public Color Accent => Visuals.Parse(secondary_color);
    }

    [Serializable]
    public class TeamDefinition
    {
        public string id, name_zh, name_en, description, primary_color, secondary_color, emblem;
        public string[] members;
    }

    [Serializable] public class RosterFile { public RacerDefinition[] items; }
    [Serializable] public class TeamsFile { public TeamDefinition[] items; }

    [Serializable]
    public class RaceRow
    {
        public string id, name_zh, name_en, color, time;
        public int rank, points, total;
        public float progress;
        public bool supported;
    }

    [Serializable]
    public class TeamRow
    {
        public string name, color;
        public int points, total;
    }

    public class RaceView
    {
        public float time, speed, progress, fps;
        public string countdown, mode, support_name, notice;
        public bool boost_used, nudge_used;
        public RaceRow[] leaderboard;
    }

    [Serializable] public class ScoreEntry { public string id; public int points; }
    [Serializable] public class RaceRecord
    {
        public string race_id, arena_id, rules_id, utc;
        public int seed;
        public RaceRow[] results;
        public string[] team_ids;
    }
    [Serializable] public class ChampionshipSave
    {
        public int schema = 1;
        public ScoreEntry[] racers = Array.Empty<ScoreEntry>();
        public ScoreEntry[] teams = Array.Empty<ScoreEntry>();
        public RaceRecord[] races = Array.Empty<RaceRecord>();
    }
}
