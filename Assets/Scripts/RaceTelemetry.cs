using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarbleArena
{
    public sealed class RaceTelemetry
    {
        const float ImpactThreshold = 1.5f;
        const float ImpactCooldown = .12f;
        const float LeaderWarmup = 3f;
        readonly Dictionary<MarbleRacer, SampleState> lookup = new Dictionary<MarbleRacer, SampleState>();
        readonly List<SampleState> samples = new List<SampleState>(12);
        readonly List<LeaderTelemetryChange> leaderEvents = new List<LeaderTelemetryChange>(32);
        float elapsed;
        float leaderBaselineTime;
        bool leaderTracking;
        MarbleRacer leader;
        string leaderBaselineId = "";

        sealed class SampleState
        {
            public MarbleRacer racer;
            public RacerTelemetryRow row;
            public Vector3 position;
            public bool validPosition;
            public bool wasFinished;
            public bool wasInBowl;
            public int lastRescues;
            public double speedIntegral;
            public double speedTime;
            public double positionTime;
            public float lastImpact = float.NegativeInfinity;
        }

        public void Reset(IEnumerable<MarbleRacer> racers)
        {
            lookup.Clear();
            samples.Clear();
            leaderEvents.Clear();
            elapsed = 0;
            leaderBaselineTime = 0;
            leaderTracking = false;
            leader = null;
            leaderBaselineId = "";
            if (racers == null) return;
            foreach (var racer in racers)
                if (racer != null && !lookup.ContainsKey(racer)) Register(racer);
        }

        public void Step(float dt, IReadOnlyList<MarbleRacer> racers)
        {
            if (!Finite(dt) || dt <= 0 || racers == null) return;
            elapsed += dt;
            MarbleRacer nextLeader = null;
            for (var i = 0; i < racers.Count; i++)
            {
                var racer = racers[i];
                if (racer == null) continue;
                if (!lookup.TryGetValue(racer, out var state)) state = Register(racer);
                var row = state.row;
                if (!state.wasFinished)
                {
                    row.active_time += dt;
                    if (racer.Finished) row.finish_intervals_excluded++;
                    else SampleMotion(state, dt);
                    if (racer.InBowl)
                    {
                        row.bowl_dwell += dt;
                        if (!state.wasInBowl) row.bowl_entries++;
                    }
                    var sector = racer.Sector ?? "";
                    if (!string.Equals(row.final_sector, sector, StringComparison.Ordinal))
                    {
                        row.sector_changes.Add(new SectorTelemetryChange
                        {
                            time = elapsed,
                            from = row.final_sector,
                            to = sector,
                            progress = Safe(racer.Progress),
                            in_bowl = racer.InBowl
                        });
                        row.final_sector = sector;
                    }
                }
                state.wasInBowl = racer.InBowl;
                state.wasFinished = racer.Finished;
                state.lastRescues = racer.Rescues;
                RefreshFinal(state);
                if (Ahead(racer, nextLeader, leader)) nextLeader = racer;
            }
            if (elapsed < LeaderWarmup)
            {
                leader = nextLeader;
                return;
            }
            if (!leaderTracking)
            {
                if (nextLeader == null) return;
                leaderTracking = true;
                leaderBaselineTime = elapsed;
                leaderBaselineId = lookup[nextLeader].row.id;
            }
            else if (leader != null && nextLeader != null && leader != nextLeader)
            {
                leaderEvents.Add(new LeaderTelemetryChange
                {
                    time = elapsed,
                    from_id = lookup[leader].row.id,
                    to_id = lookup[nextLeader].row.id,
                    progress = Safe(nextLeader.Progress),
                    finished = nextLeader.Finished,
                    rank = nextLeader.Rank
                });
            }
            leader = nextLeader;
        }

        public void RecordImpact(MarbleRacer racer, float force)
        {
            if (racer == null || racer.Finished || !Finite(force) || force <= ImpactThreshold) return;
            if (!lookup.TryGetValue(racer, out var state)) state = Register(racer);
            state.row.raw_impact_events++;
            state.row.max_impact_relative_speed = Mathf.Max(state.row.max_impact_relative_speed, force);
            if (elapsed - state.lastImpact < ImpactCooldown)
            {
                state.row.suppressed_impact_events++;
                return;
            }
            state.lastImpact = elapsed;
            state.row.impact_events++;
        }

        public RaceTelemetrySummary Summary()
        {
            var result = new RaceTelemetrySummary
            {
                elapsed = elapsed,
                leader_changes = leaderEvents.Count,
                leader_baseline_time = leaderBaselineTime,
                leader_baseline_id = leaderBaselineId
            };
            double totalSpeedIntegral = 0;
            double totalSpeedTime = 0;
            double totalPositionTime = 0;
            for (var i = 0; i < samples.Count; i++)
            {
                var state = samples[i];
                RefreshFinal(state);
                var row = Snapshot(state);
                result.racers.Add(row);
                result.max_speed = Mathf.Max(result.max_speed, row.max_speed);
                result.first_5s_max_speed = Mathf.Max(result.first_5s_max_speed, row.first_5s_max_speed);
                result.max_displacement_speed = Mathf.Max(result.max_displacement_speed, row.max_displacement_speed);
                result.displacement_distance += row.displacement_distance;
                result.recovery_displacement += row.recovery_displacement;
                result.finish_intervals_excluded += row.finish_intervals_excluded;
                result.bowl_dwell += row.bowl_dwell;
                result.impact_events += row.impact_events;
                result.raw_impact_events += row.raw_impact_events;
                result.suppressed_impact_events += row.suppressed_impact_events;
                result.max_impact_relative_speed = Mathf.Max(result.max_impact_relative_speed, row.max_impact_relative_speed);
                result.invalid_motion_samples += row.invalid_motion_samples;
                result.finished += row.finished ? 1 : 0;
                result.rescues += row.rescues;
                totalSpeedIntegral += state.speedIntegral;
                totalSpeedTime += state.speedTime;
                totalPositionTime += state.positionTime;
            }
            result.mean_speed = totalSpeedTime > 0 ? (float)(totalSpeedIntegral / totalSpeedTime) : 0;
            result.mean_displacement_speed = totalPositionTime > 0 ? (float)(result.displacement_distance / totalPositionTime) : 0;
            for (var i = 0; i < leaderEvents.Count; i++)
            {
                var item = leaderEvents[i];
                result.leader_events.Add(new LeaderTelemetryChange
                {
                    time = item.time, from_id = item.from_id, to_id = item.to_id,
                    progress = item.progress, finished = item.finished, rank = item.rank
                });
            }
            return result;
        }

        SampleState Register(MarbleRacer racer)
        {
            var position = racer.Body != null ? racer.Body.position : racer.transform.position;
            var state = new SampleState
            {
                racer = racer,
                position = position,
                validPosition = Finite(position),
                wasFinished = racer.Finished,
                wasInBowl = racer.InBowl,
                lastRescues = racer.Rescues,
                row = new RacerTelemetryRow
                {
                    id = racer.Definition != null ? racer.Definition.id : racer.name,
                    name = racer.Definition != null ? racer.Definition.name_zh : racer.name,
                    final_sector = racer.Sector ?? "",
                    bowl_entries = racer.InBowl ? 1 : 0
                }
            };
            if (!string.IsNullOrEmpty(state.row.final_sector))
                state.row.sector_changes.Add(new SectorTelemetryChange
                {
                    time = elapsed, from = "", to = state.row.final_sector,
                    progress = Safe(racer.Progress), in_bowl = racer.InBowl
                });
            RefreshFinal(state);
            lookup.Add(racer, state);
            samples.Add(state);
            return state;
        }

        void SampleMotion(SampleState state, float dt)
        {
            var row = state.row;
            var body = state.racer.Body;
            if (body == null)
            {
                row.invalid_motion_samples++;
                state.validPosition = false;
                return;
            }
            var velocity = body.linearVelocity.magnitude;
            if (Finite(velocity))
            {
                state.speedIntegral += velocity * (double)dt;
                state.speedTime += dt;
                row.max_speed = Mathf.Max(row.max_speed, velocity);
                if (elapsed <= 5f) row.first_5s_max_speed = Mathf.Max(row.first_5s_max_speed, velocity);
            }
            else row.invalid_motion_samples++;
            var position = body.position;
            if (Finite(position))
            {
                if (state.validPosition)
                {
                    var distance = Vector3.Distance(position, state.position);
                    if (!Finite(distance)) row.invalid_motion_samples++;
                    else if (state.racer.Rescues != state.lastRescues)
                    {
                        row.recovery_displacement += distance;
                        row.recovery_intervals_excluded++;
                    }
                    else
                    {
                        row.displacement_distance += distance;
                        row.max_displacement_speed = Mathf.Max(row.max_displacement_speed, distance / dt);
                        state.positionTime += dt;
                    }
                }
                state.position = position;
                state.validPosition = true;
            }
            else
            {
                row.invalid_motion_samples++;
                state.validPosition = false;
            }
        }

        static void RefreshFinal(SampleState state)
        {
            if (state.racer == null) return;
            state.row.finished = state.racer.Finished;
            state.row.finish_time = Safe(state.racer.FinishTime);
            state.row.rescues = state.racer.Rescues;
            state.row.final_rank = state.racer.Rank;
            state.row.final_progress = Safe(state.racer.Progress);
        }

        static RacerTelemetryRow Snapshot(SampleState state)
        {
            var row = state.row;
            var result = new RacerTelemetryRow
            {
                id = row.id, name = row.name, active_time = row.active_time,
                max_speed = row.max_speed,
                mean_speed = state.speedTime > 0 ? (float)(state.speedIntegral / state.speedTime) : 0,
                first_5s_max_speed = row.first_5s_max_speed,
                velocity_sample_time = (float)state.speedTime,
                displacement_sample_time = (float)state.positionTime,
                displacement_distance = row.displacement_distance,
                max_displacement_speed = row.max_displacement_speed,
                mean_displacement_speed = state.positionTime > 0 ? (float)(row.displacement_distance / state.positionTime) : 0,
                recovery_displacement = row.recovery_displacement,
                recovery_intervals_excluded = row.recovery_intervals_excluded,
                finish_intervals_excluded = row.finish_intervals_excluded,
                bowl_dwell = row.bowl_dwell, bowl_entries = row.bowl_entries,
                impact_events = row.impact_events, raw_impact_events = row.raw_impact_events,
                suppressed_impact_events = row.suppressed_impact_events,
                max_impact_relative_speed = row.max_impact_relative_speed,
                invalid_motion_samples = row.invalid_motion_samples,
                finished = row.finished, finish_time = row.finish_time, rescues = row.rescues,
                final_rank = row.final_rank, final_progress = row.final_progress, final_sector = row.final_sector
            };
            for (var i = 0; i < row.sector_changes.Count; i++)
            {
                var item = row.sector_changes[i];
                result.sector_changes.Add(new SectorTelemetryChange
                {
                    time = item.time, from = item.from, to = item.to,
                    progress = item.progress, in_bowl = item.in_bowl
                });
            }
            return result;
        }

        static bool Ahead(MarbleRacer candidate, MarbleRacer current, MarbleRacer previous)
        {
            if (current == null) return true;
            if (candidate.Finished != current.Finished) return candidate.Finished;
            if (candidate.Finished)
            {
                var candidateRank = candidate.Rank > 0 ? candidate.Rank : int.MaxValue;
                var currentRank = current.Rank > 0 ? current.Rank : int.MaxValue;
                if (candidateRank != currentRank) return candidateRank < currentRank;
                if (Mathf.Abs(candidate.FinishTime - current.FinishTime) > .0001f)
                    return candidate.FinishTime < current.FinishTime;
            }
            else
            {
                var candidateProgress = Finite(candidate.Progress) ? candidate.Progress : float.NegativeInfinity;
                var currentProgress = Finite(current.Progress) ? current.Progress : float.NegativeInfinity;
                if (Mathf.Abs(candidateProgress - currentProgress) > .0001f)
                    return candidateProgress > currentProgress;
            }
            return candidate == previous && current != previous;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static float Safe(float value) => Finite(value) ? value : 0;
    }

    [Serializable]
    public sealed class RaceTelemetrySummary
    {
        public int schema = 1;
        public string speed_unit = "m/s";
        public string time_unit = "s";
        public string distance_unit = "m";
        public string mean_speed_basis = "Time-weighted Rigidbody.linearVelocity magnitude before each racer finishes; invalid samples and newly-finished intervals excluded.";
        public string displacement_basis = "Successive Rigidbody.position samples; newly-finished intervals are excluded to avoid the finish-stand teleport. Intervals with a Rescues change are excluded and reported separately.";
        public string impact_counting = "Per-racer callbacks with relative speed >1.5 m/s and a 0.12 s cooldown. Opponent identity is unavailable: walls can count, a pair can count twice, and rapid distinct contacts can be suppressed. Counts are not unique marble collisions.";
        public string impact_source_note = "Fed by MarbleRacer.PhysicalImpact from raw OnCollisionEnter, independently of audio cooldown. No contact-opponent identity is supplied.";
        public float elapsed, max_speed, mean_speed, first_5s_max_speed;
        public float displacement_distance, mean_displacement_speed, max_displacement_speed, recovery_displacement;
        public float bowl_dwell, max_impact_relative_speed;
        public float leader_warmup = 3f;
        public float leader_baseline_time;
        public string leader_baseline_id = "";
        public int impact_events, raw_impact_events, suppressed_impact_events, invalid_motion_samples, finish_intervals_excluded;
        public int leader_changes, finished, rescues;
        public List<RacerTelemetryRow> racers = new List<RacerTelemetryRow>();
        public List<LeaderTelemetryChange> leader_events = new List<LeaderTelemetryChange>();
    }

    [Serializable]
    public sealed class RacerTelemetryRow
    {
        public string id, name, final_sector;
        public float active_time, max_speed, mean_speed, first_5s_max_speed, velocity_sample_time;
        public float displacement_sample_time, displacement_distance, mean_displacement_speed, max_displacement_speed;
        public float recovery_displacement, bowl_dwell, max_impact_relative_speed;
        public int recovery_intervals_excluded, finish_intervals_excluded, bowl_entries, impact_events, raw_impact_events, suppressed_impact_events;
        public int invalid_motion_samples, rescues, final_rank;
        public bool finished;
        public float finish_time = -1, final_progress;
        public List<SectorTelemetryChange> sector_changes = new List<SectorTelemetryChange>();
    }

    [Serializable]
    public sealed class SectorTelemetryChange
    {
        public float time, progress;
        public string from, to;
        public bool in_bowl;
    }

    [Serializable]
    public sealed class LeaderTelemetryChange
    {
        public float time, progress;
        public string from_id, to_id;
        public bool finished;
        public int rank;
    }
}
