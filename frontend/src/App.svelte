<script lang="ts">
    import { onMount } from 'svelte';
    import { startConnection } from './lib/stores/connection.ts';
    import { missions } from './lib/stores/missions.ts';
    import MissionPanel from './lib/components/MissionPanel.svelte';
    import RoutePanel from './lib/components/RoutePanel.svelte';
    import StatusBar from './lib/components/StatusBar.svelte';

    onMount(() => {
        startConnection();
    });
</script>

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

        <!-- Right Column: Optimized Route -->
        <div class="panel-column right-column">
            <RoutePanel />
        </div>
    </div>

    <StatusBar />
</div>
