<script lang="ts">
    import { onMount } from 'svelte';
    import { startConnection } from './lib/stores/connection.ts';
    import { missions } from './lib/stores/missions.ts';
    import MissionPanel from './lib/components/MissionPanel.svelte';
    import RoutePanel from './lib/components/RoutePanel.svelte';
    import CargoPanel from './lib/components/CargoPanel.svelte';
    import StatusBar from './lib/components/StatusBar.svelte';
    import ToastContainer from './lib/components/ToastContainer.svelte';
    import { fade } from 'svelte/transition';

    onMount(() => {
        startConnection();
    });

    $: totalCargoScu = Array.from($missions.values()).reduce((sum, mission) => {
        if (mission.status?.Case !== 'Active') return sum;
        if (mission.missionType?.Case === 'Courier') return sum;
        
        const pickupScu = (mission.objectives || [])
            .filter((o: any) => o.type?.Case === 'Pickup')
            .reduce((acc: number, o: any) => acc + (o.scuAmount || 0), 0);
        const dropoffScu = (mission.objectives || [])
            .filter((o: any) => o.type?.Case === 'Dropoff')
            .reduce((acc: number, o: any) => acc + (o.scuAmount || 0), 0);
            
        return sum + Math.max(pickupScu, dropoffScu);
    }, 0);

    $: showCargoPanel = totalCargoScu > 1;
</script>

<ToastContainer />

<div class="unsupported-resolution">
    <div class="glass-panel message-box">
        <h2>Resolution Not Supported</h2>
        <p>Please use a larger screen or browser window.</p>
        <p class="sub">You may need to rotate your device to landscape mode.</p>
    </div>
</div>

<div id="app">
    <div class="main-content">
        <!-- Left Column: Settings and Missions -->
        <div class="panel-column left-column">
            <MissionPanel />
        </div>

        <!-- Middle Column: Optimized Route -->
        <div class="panel-column right-column">
            <RoutePanel />
        </div>

        <!-- Right Column: Cargo Batches (Conditionally shown when cargo SCU > 1) -->
        {#if showCargoPanel}
            <div class="panel-column cargo-column" transition:fade={{ duration: 250 }}>
                <CargoPanel />
            </div>
        {/if}
    </div>

    <StatusBar />
</div>

