<script lang="ts">
    import { connectionStatus } from '../stores/connection.ts';
    import { route } from '../stores/route.ts';
    import { missions } from '../stores/missions.ts';
    import { zoomStore, MIN_ZOOM, MAX_ZOOM } from '../stores/zoom.ts';
    import { Activity, Database, Settings2, X, Package, Plus, Minus } from 'lucide-svelte';
    import ShipConfig from './ShipConfig.svelte';
    import { fly } from 'svelte/transition';

    let isConfigOpen = false;

    function toggleConfig() {
        isConfigOpen = !isConfigOpen;
    }

    let onboardScu = 0;
    let onboardPackages = 0;

    $: {
        let scu = 0;
        let pkg = 0;
        
        for (const mission of $missions.values()) {
            if (mission.status?.Case !== 'Active') continue;
            
            let missionScuPickedUp = 0;
            let missionScuDropped = 0;
            let missionPkgPickedUp = 0;
            let missionPkgDropped = 0;
            
            for (const obj of mission.objectives || []) {
                if (obj.status?.Case === 'Completed') {
                    if (obj.type?.Case === 'Pickup') {
                        if (obj.scuAmount) missionScuPickedUp += obj.scuAmount;
                        else missionPkgPickedUp += 1;
                    } else if (obj.type?.Case === 'Dropoff') {
                        if (obj.scuAmount) missionScuDropped += obj.scuAmount;
                        else missionPkgDropped += 1;
                    }
                }
            }
            
            scu += Math.max(0, missionScuPickedUp - missionScuDropped);
            pkg += Math.max(0, missionPkgPickedUp - missionPkgDropped);
        }
        
        onboardScu = scu;
        onboardPackages = pkg;
    }
</script>

<div class="status-bar glass-panel">
    <div class="status-item">
        <Activity size={16} color={$connectionStatus.state === 'Connected' ? 'var(--accent-green)' : 'var(--accent-danger)'} />
        <span class="label">SignalR:</span>
        <span class="value" class:connected={$connectionStatus.state === 'Connected'}>
            {$connectionStatus.state}
        </span>
    </div>
    
    <div class="divider"></div>
    
    <div class="status-item">
        <Database size={16} class="icon" />
        <span class="label">Game.log:</span>
        <span class="value path" title={$connectionStatus.logPath || 'Not found'}>
            {$connectionStatus.logPath ? $connectionStatus.logPath.split(/[\\/]/).pop() : 'Waiting...'}
        </span>
    </div>

    <div class="divider"></div>
    
    <div class="status-item">
        <Package size={16} class="icon" />
        <span class="label">Cargo Onboard:</span>
        <span class="value">
            {#if onboardScu > 0 && onboardPackages > 0}
                {onboardScu} SCU & {onboardPackages} {onboardPackages === 1 ? 'package' : 'packages'}
            {:else if onboardScu > 0}
                {onboardScu} SCU
            {:else if onboardPackages > 0}
                {onboardPackages} {onboardPackages === 1 ? 'package' : 'packages'}
            {:else}
                Empty
            {/if}
        </span>
    </div>
    
    <div class="spacer"></div>
    
    <div class="zoom-controls">
        <button 
            class="zoom-btn" 
            on:click={() => zoomStore.zoomOut()} 
            title="Уменьшить масштаб" 
            disabled={$zoomStore <= MIN_ZOOM}
        >
            <Minus size={14} />
        </button>
        <button 
            class="zoom-indicator" 
            on:click={() => zoomStore.reset()} 
            title="Сбросить масштаб (100%)"
        >
            {$zoomStore}%
        </button>
        <button 
            class="zoom-btn" 
            on:click={() => zoomStore.zoomIn()} 
            title="Увеличить масштаб" 
            disabled={$zoomStore >= MAX_ZOOM}
        >
            <Plus size={14} />
        </button>
    </div>

    <div class="divider"></div>

    <button class="config-btn" on:click={toggleConfig} class:active={isConfigOpen}>
        <Settings2 size={16} />
        <span>Ship Config</span>
    </button>
    
    {#if isConfigOpen}
        <div class="config-popup" transition:fly={{y: 10, duration: 200}}>
            <button class="close-btn" on:click={toggleConfig}>
                <X size={18} />
            </button>
            <ShipConfig />
        </div>
    {/if}
</div>

<style>
    .status-bar {
        display: flex;
        align-items: center;
        padding: 0.75rem 1.5rem;
        margin: 0 1rem 1rem 1rem;
        gap: 1.5rem;
        border-radius: 4px;
        background: rgba(13, 22, 30, 0.85);
        position: relative;
    }
    
    .status-item {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        font-size: 0.9rem;
    }
    
    .label {
        color: var(--text-muted);
    }
    
    .value {
        font-weight: 600;
        color: var(--text-main);
    }
    
    .connected {
        color: var(--accent-green);
    }
    
    .icon {
        color: var(--accent-teal);
    }
    
    .path {
        font-family: monospace;
        background: rgba(0,0,0,0.3);
        padding: 0.2rem 0.5rem;
        border-radius: 4px;
        font-size: 0.85rem;
    }
    
    .divider {
        width: 1px;
        height: 1rem;
        background: var(--bg-panel-border);
    }

    .spacer {
        flex: 1;
    }

    .zoom-controls {
        display: flex;
        align-items: center;
        background: rgba(255, 255, 255, 0.04);
        border: 1px solid var(--bg-panel-border);
        border-radius: 4px;
        padding: 0.15rem 0.25rem;
        gap: 0.2rem;
    }

    .zoom-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        background: transparent;
        border: none;
        color: var(--text-muted);
        width: 22px;
        height: 22px;
        border-radius: 3px;
        cursor: pointer;
        transition: var(--transition);
    }

    .zoom-btn:hover:not(:disabled) {
        background: rgba(0, 255, 255, 0.15);
        color: var(--accent-cyan);
    }

    .zoom-btn:disabled {
        opacity: 0.3;
        cursor: not-allowed;
    }

    .zoom-indicator {
        background: transparent;
        border: none;
        color: var(--text-main);
        font-family: monospace;
        font-size: 0.85rem;
        font-weight: 600;
        min-width: 42px;
        text-align: center;
        padding: 0 0.25rem;
        cursor: pointer;
        transition: var(--transition);
        border-radius: 3px;
    }

    .zoom-indicator:hover {
        color: var(--accent-cyan);
        background: rgba(255, 255, 255, 0.05);
    }
    
    .config-btn {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        background: rgba(255, 255, 255, 0.05);
        border: 1px solid var(--bg-panel-border);
        color: var(--text-main);
        padding: 0.4rem 0.75rem;
        border-radius: 4px;
        cursor: pointer;
        transition: var(--transition);
        font-family: inherit;
        font-size: 0.9rem;
        font-weight: 600;
    }
    
    .config-btn:hover, .config-btn.active {
        background: rgba(13, 148, 136, 0.15);
        border-color: var(--accent-cyan);
        color: var(--accent-cyan);
    }
    
    .config-popup {
        position: absolute;
        bottom: calc(100% + 10px);
        right: 1.5rem;
        width: 320px;
        z-index: 100;
        box-shadow: 0 10px 40px rgba(0,0,0,0.8);
        border-radius: 6px;
    }
    
    .close-btn {
        position: absolute;
        top: 1rem;
        right: 1rem;
        background: none;
        border: none;
        color: var(--text-muted);
        cursor: pointer;
        z-index: 101;
        padding: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        transition: color 0.2s;
    }
    
    .close-btn:hover {
        color: var(--text-main);
    }
</style>
