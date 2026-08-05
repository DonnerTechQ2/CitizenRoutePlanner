<script lang="ts">
    import { MapPin, Navigation, ArrowDownToLine, ArrowUpFromLine, Clock } from 'lucide-svelte';
    import { hoveredMissionIds } from '../stores/missions.ts';

    export let stop;
    export let index = 0;
    export let isCurrent = false;
    export let isLast = false;
    export let nextTravelTime = 0;

    // Estimate time logic (mocking a formatter)
    function formatTime(seconds) {
        if (seconds < 60) return `${Math.round(seconds)}s`;
        const m = Math.floor(seconds / 60);
        const s = Math.round(seconds % 60);
        return `${m}m ${s}s`;
    }
    
    $: isHighlighted = stop.actions.some(action => $hoveredMissionIds.has(action.Fields?.[0] || action.missionId));

    function handleMouseEnter() {
        const ids = new Set(stop.actions.map(a => a.Fields?.[0] || a.missionId).filter(Boolean));
        hoveredMissionIds.set(ids);
    }

    function handleMouseLeave() {
        hoveredMissionIds.set(new Set());
    }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div class="route-stop" 
     class:current={isCurrent} 
     class:highlighted={isHighlighted}
     on:mouseenter={handleMouseEnter}
     on:mouseleave={handleMouseLeave}>
    <div class="timeline">
        <div class="node-icon">
            <MapPin size={16} />
        </div>
        {#if !isLast}
            <div class="connector">
                {#if nextTravelTime > 0}
                    <div class="travel-time">
                        <Navigation size={12} class="nav-icon" />
                        <span>{formatTime(nextTravelTime)}</span>
                    </div>
                {/if}
            </div>
        {/if}
    </div>
    
    <div class="content glass-panel">
        <div class="stop-header">
            <span class="index-badge">{index + 1}</span>
            <h3>{stop.location?.name || 'Unknown Location'}</h3>
            {#if stop.actionTimeEstimate > 0}
                <span class="time-tag" title="Estimated time for actions (landing, walking, loading)">
                    <Clock size={12} class="clock-icon" />
                    {formatTime(stop.actionTimeEstimate)}
                </span>
            {/if}
            <span class="system-tag">{stop.location?.type || 'Point of Interest'}</span>
        </div>
        
        <div class="actions-list">
            {#each stop.actions as action}
                {@const isPickup = action.Case === 'PickupCargo' || action.Case === 'PickupPackage'}
                {@const isDropoff = action.Case === 'DropoffCargo' || action.Case === 'DropoffPackage'}
                {@const isNav = action.Case === 'NavTo'}
                {@const hasScu = action.Fields?.[2] || action.scuAmount}
                <div class="action-item" class:pickup={isPickup} class:dropoff={isDropoff} class:nav={isNav}>
                    <div class="action-icon">
                        {#if isPickup}
                            <ArrowUpFromLine size={14} />
                        {:else if isDropoff}
                            <ArrowDownToLine size={14} />
                        {:else}
                            <Navigation size={14} />
                        {/if}
                    </div>
                    <div class="action-details">
                        <span class="action-type">{isPickup ? 'PICKUP' : (isDropoff ? 'DROPOFF' : 'NAV')}</span>
                        {#if hasScu}
                            <span class="scu-pill">{hasScu} SCU</span>
                        {/if}
                    </div>
                </div>
            {/each}
        </div>
    </div>
</div>

<style>
    .route-stop {
        display: flex;
        gap: 1.75rem;
        margin-left: 0.75rem;
        margin-bottom: 0.5rem;
        opacity: 0.8;
        transition: var(--transition);
    }
    
    .route-stop.current {
        opacity: 1;
    }
    
    .route-stop.highlighted .content {
        box-shadow: 0 0 15px rgba(0, 255, 255, 0.2);
        border-color: var(--accent-cyan-dim);
    }

    .timeline {
        display: flex;
        flex-direction: column;
        align-items: center;
        width: 40px;
    }
    
    .node-icon {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        background: rgba(13, 22, 30, 0.9);
        border: 2px solid var(--text-muted);
        display: flex;
        align-items: center;
        justify-content: center;
        color: var(--text-muted);
        z-index: 2;
        transition: var(--transition);
    }
    
    .route-stop.current .node-icon {
        border-color: var(--accent-cyan);
        color: var(--accent-cyan);
        box-shadow: 0 0 12px var(--accent-cyan-dim);
    }
    
    .connector {
        flex: 1;
        width: 2px;
        background: var(--bg-panel-border);
        margin: 0.25rem 0;
        position: relative;
        min-height: 40px;
    }
    
    .route-stop.current .connector {
        background: linear-gradient(to bottom, var(--accent-cyan), rgba(0, 255, 255, 0.1));
    }
    
    .travel-time {
        position: absolute;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        background: var(--bg-color);
        border: 1px solid var(--bg-panel-border);
        padding: 0.2rem 0.5rem;
        border-radius: 12px;
        font-size: 0.75rem;
        color: var(--text-muted);
        display: flex;
        align-items: center;
        gap: 0.25rem;
        white-space: nowrap;
        z-index: 3;
    }
    
    .content {
        flex: 1;
        padding: 1rem;
        margin-bottom: 1rem;
        border-left: 3px solid transparent;
        min-width: 0;
    }
    
    .route-stop.current .content {
        border-left-color: var(--accent-cyan);
        background: var(--bg-panel-hover);
    }
    
    .route-stop.highlighted .content {
        background: var(--bg-panel-hover);
        border-left-color: var(--accent-cyan-dim);
    }
    
    .stop-header {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        margin-bottom: 1rem;
        min-width: 0;
    }
    
    .index-badge {
        background: rgba(255, 255, 255, 0.1);
        color: var(--text-main);
        font-weight: 700;
        font-family: var(--font-heading);
        width: 24px;
        height: 24px;
        display: flex;
        align-items: center;
        justify-content: center;
        border-radius: 4px;
        font-size: 0.9rem;
    }
    
    .route-stop.current .index-badge {
        background: var(--accent-cyan);
        color: #000;
    }
    
    h3 {
        margin: 0;
        font-size: 1.15rem;
        flex: 1;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
    }
    
    .system-tag {
        font-size: 0.75rem;
        color: var(--text-muted);
        text-transform: uppercase;
        letter-spacing: 0.5px;
        border: 1px solid rgba(255,255,255,0.1);
        padding: 0.1rem 0.4rem;
        border-radius: 3px;
    }
    
    .time-tag {
        font-size: 0.8rem;
        color: var(--accent-teal);
        display: flex;
        align-items: center;
        gap: 0.25rem;
        background: rgba(0, 255, 255, 0.05);
        border: 1px solid rgba(0, 255, 255, 0.1);
        padding: 0.1rem 0.5rem;
        border-radius: 4px;
        margin-right: 0.5rem;
    }
    
    .actions-list {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
    }
    
    .action-item {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        background: rgba(0,0,0,0.2);
        padding: 0.5rem 0.75rem;
        border-radius: 4px;
        border-left: 2px solid;
    }
    
    .action-item.pickup {
        border-left-color: var(--accent-warning);
    }
    
    .action-item.dropoff {
        border-left-color: var(--accent-green);
    }

    .action-item.nav {
        border-left-color: var(--accent-cyan);
    }
    
    .action-icon {
        display: flex;
        align-items: center;
        justify-content: center;
    }
    
    .action-item.pickup .action-icon { color: var(--accent-warning); }
    .action-item.dropoff .action-icon { color: var(--accent-green); }
    .action-item.nav .action-icon { color: var(--accent-cyan); }
    
    .action-details {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        flex: 1;
    }
    
    .action-type {
        font-weight: 600;
        font-size: 0.85rem;
        letter-spacing: 0.5px;
    }
    
    .scu-pill {
        background: rgba(255,255,255,0.1);
        padding: 0.1rem 0.4rem;
        border-radius: 10px;
        font-size: 0.75rem;
        font-weight: 600;
    }
</style>
