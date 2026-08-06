import { writable } from 'svelte/store';
import { connection } from './connection';

export const route = writable<any>(null);
export const playerLocation = writable<any>(null);
export const ship = writable<any>(null);
export const quantumDrive = writable<any>(null);

connection.on("RouteUpdated", (updatedRoute: any) => {
    route.set(updatedRoute);
});

connection.on("PlayerLocationUpdated", (location: any) => {
    playerLocation.set(location);
});

export function updateShip(stats: any) {
    ship.set(stats);
    if (connection.state === "Connected") {
        connection.invoke("SetShip", stats).catch(err => console.error(err));
    }
}

export function updateQuantumDrive(stats: any) {
    quantumDrive.set(stats);
    if (connection.state === "Connected") {
        connection.invoke("SetQuantumDrive", stats).catch(err => console.error(err));
    }
}
