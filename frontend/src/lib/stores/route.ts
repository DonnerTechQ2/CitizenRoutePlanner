import { writable } from 'svelte/store';
import { connection } from './connection';

export const route = writable<any>(null);
export const playerLocation = writable<any>(null);
export const shipCapacity = writable<number>(16); // Default SCU
export const shipSpeedModifier = writable<number>(1.0); // Default multiplier

connection.on("RouteUpdated", (updatedRoute: any) => {
    route.set(updatedRoute);
});

connection.on("PlayerLocationUpdated", (location: any) => {
    playerLocation.set(location);
});

export function updateShipCapacity(scu: number) {
    shipCapacity.set(scu);
    if (connection.state === "Connected") {
        connection.invoke("SetShipCapacity", scu).catch(err => console.error(err));
    }
}

export function updateShipSpeedModifier(modf: number) {
    shipSpeedModifier.set(modf);
    if (connection.state === "Connected") {
        connection.invoke("SetShipSpeedModifier", modf).catch(err => console.error(err));
    }
}
