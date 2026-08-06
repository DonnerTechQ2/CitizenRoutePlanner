import { writable } from 'svelte/store';
import { connection, connectionStatus } from './connection';
import { triggerUntrackWarningNotification } from './notifications';

export const missions = writable<Map<string, any>>(new Map());
export const hoveredMissionIds = writable<Set<string>>(new Set());

let initialSyncDone = false;
let syncTimer: any = null;

connectionStatus.subscribe(status => {
    if (status.state === 'Connected') {
        if (syncTimer) clearTimeout(syncTimer);
        syncTimer = setTimeout(() => {
            initialSyncDone = true;
        }, 1000);
    } else {
        initialSyncDone = false;
    }
});

// SignalR Events for missions
connection.on("MissionAdded", (mission: any) => {
    let isNewMission = false;
    missions.update(m => {
        if (!m.has(mission.missionId)) {
            isNewMission = true;
        }
        const newMap = new Map(m);
        newMap.set(mission.missionId, mission);
        return newMap;
    });

    if (initialSyncDone && isNewMission) {
        const title = mission.title || mission.contractName || undefined;
        triggerUntrackWarningNotification(title);
    }
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
});

