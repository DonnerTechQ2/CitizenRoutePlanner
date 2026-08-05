import { writable } from 'svelte/store';
import { connection, connectionStatus } from './connection';

export const missions = writable<Map<string, any>>(new Map());
export const hoveredMissionIds = writable<Set<string>>(new Set());

// SignalR Events for missions
connection.on("MissionAdded", (mission: any) => {
    missions.update(m => {
        const newMap = new Map(m);
        newMap.set(mission.missionId, mission);
        return newMap;
    });
    connectionStatus.update(s => ({ ...s, missionCount: s.missionCount + 1 }));
});

connection.on("MissionUpdated", (mission: any) => {
    missions.update(m => {
        const newMap = new Map(m);
        newMap.set(mission.missionId, mission);
        return newMap;
    });
});

connection.on("MissionRemoved", (missionId: string) => {
    missions.update(m => {
        const newMap = new Map(m);
        newMap.delete(missionId);
        return newMap;
    });
    connectionStatus.update(s => ({ ...s, missionCount: Math.max(0, s.missionCount - 1) }));
});
