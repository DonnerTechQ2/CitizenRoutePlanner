import { writable } from 'svelte/store';
import * as signalR from '@microsoft/signalr';

export interface ConnectionStatus {
    state: string;
    logPath: string | null;
}

export const connectionStatus = writable<ConnectionStatus>({
    state: 'Disconnected',
    logPath: null
});

// Configure SignalR connection
export const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/route")
    .withAutomaticReconnect()
    .build();

// Start connection
export async function startConnection() {
    try {
        connectionStatus.update(s => ({ ...s, state: 'Connecting' }));
        await connection.start();
        connectionStatus.update(s => ({ ...s, state: 'Connected' }));
        console.log("SignalR Connected.");
    } catch (err) {
        console.log("SignalR Connection Error: ", err);
        connectionStatus.update(s => ({ ...s, state: 'Disconnected' }));
        setTimeout(startConnection, 5000);
    }
}

connection.onreconnecting(error => {
    connectionStatus.update(s => ({ ...s, state: 'Reconnecting' }));
});

connection.onreconnected(connectionId => {
    connectionStatus.update(s => ({ ...s, state: 'Connected' }));
});

connection.onclose(error => {
    connectionStatus.update(s => ({ ...s, state: 'Disconnected' }));
});

connection.on("ConnectionStatus", (status) => {
    connectionStatus.update(s => ({ ...s, logPath: status.logPath }));
});
