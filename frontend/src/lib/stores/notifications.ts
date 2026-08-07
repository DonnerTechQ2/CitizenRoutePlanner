import { writable } from 'svelte/store';

export interface ToastNotification {
    id: string;
    title: string;
    message: string;
    missionTitle?: string;
    duration: number; // in milliseconds, default 10000
    createdAt: number;
}

export const notifications = writable<ToastNotification[]>([]);

export function addNotification(
    title: string,
    message: string,
    missionTitle?: string,
    duration = 10000
): string {
    const id = Math.random().toString(36).substring(2, 9);
    const toast: ToastNotification = {
        id,
        title,
        message,
        missionTitle,
        duration,
        createdAt: Date.now()
    };

    notifications.update(n => [...n, toast]);

    if (duration > 0) {
        setTimeout(() => {
            dismissNotification(id);
        }, duration);
    }

    return id;
}

export function dismissNotification(id: string) {
    notifications.update(n => n.filter(t => t.id !== id));
}

export function triggerUntrackWarningNotification(missionTitle?: string) {
    addNotification(
        "MISSION ACCEPTED // UNTRACK WARNING",
        "Remember to untrack this mission in mobiGlas! Active tracking prevents objective data from loading on your next pickup.",
        missionTitle,
        10000
    );
}

// Expose helper to window for dev testing
if (typeof window !== 'undefined') {
    (window as any).testUntrackNotification = (title = "Sample Delivery Mission") => {
        triggerUntrackWarningNotification(title);
    };
}

