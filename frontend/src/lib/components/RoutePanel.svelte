<script lang="ts">
    import { route } from '../stores/route.ts';
    import RouteStop from './RouteStop.svelte';
    import { Waypoints, Clock } from 'lucide-svelte';
    import { fade } from 'svelte/transition';

    $: currentIndex = $route?.currentStopIndex || 0;
    
    function formatTime(seconds) {
        if (!seconds) return "0m";
        const m = Math.floor(seconds / 60);
        const s = Math.round(seconds % 60);
        return `${m}m ${s}s`;
    }
</script>

<div class="route-panel">
    <div class="panel-header">
        <Waypoints size={22} class="icon" />
        <h2>Optimized Route</h2>
        
        {#if $route && $route.totalEstimatedTime > 0}
            <div class="total-time">
                <Clock size={14} />
                <span>ETA: {formatTime($route.totalEstimatedTime)}</span>
            </div>
        {/if}
    </div>

    <div class="route-timeline">
        {#if !$route || !$route.stops || $route.stops.length === 0}
            <div class="empty-state">
                <div class="glow-orb"></div>
                <p>Route is empty.</p>
                <p class="sub">Waiting for active missions to generate an optimal path...</p>
            </div>
        {:else}
            {#each $route.stops as stop, index}
                <div transition:fade={{duration: 200}}>
                    <RouteStop 
                        {stop} 
                        {index} 
                        isCurrent={index === currentIndex} 
                        isLast={index === $route.stops.length - 1}
                        nextTravelTime={$route.stops[index + 1]?.travelTimeEstimate || 0}
                    />
                </div>
            {/each}
        {/if}
    </div>
</div>

<style>
    .route-panel {
        display: flex;
        flex-direction: column;
        height: 100%;
        gap: 1rem;
    }
    
    .panel-header {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        padding-bottom: 0.5rem;
        border-bottom: 1px solid var(--bg-panel-border);
    }
    
    .panel-header h2 {
        margin: 0;
        color: var(--accent-teal);
        flex: 1;
    }
    
    .icon {
        color: var(--accent-teal);
    }
    
    .total-time {
        display: flex;
        align-items: center;
        gap: 0.4rem;
        background: rgba(13, 148, 136, 0.15);
        border: 1px solid rgba(13, 148, 136, 0.3);
        padding: 0.3rem 0.75rem;
        border-radius: 4px;
        color: var(--accent-teal);
        font-weight: 600;
        font-size: 0.9rem;
    }
    
    .route-timeline {
        flex: 1;
        overflow-y: auto;
        scrollbar-gutter: stable;
        padding: 0.5rem;
        position: relative;
    }
    
    .empty-state {
        height: 100%;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        text-align: center;
        color: var(--text-muted);
    }
    

    .glow-orb {
        width: 120px;
        height: 120px;
        border-radius: 50%;
        background: radial-gradient(circle, rgba(13, 148, 136, 0.35) 0%, transparent 70%);
        box-shadow: 0 0 20px rgba(13, 148, 136, 0.5);
        margin-bottom: 1rem;
        animation: pulse-glow 6s infinite ease-in-out;
    }
    
    .empty-state p {
        margin: 0;
        font-size: 1.1rem;
    }
    
    @keyframes pulse-glow {
        0% { transform: scale(0.95); opacity: 0.6; box-shadow: 0 0 15px rgba(13, 148, 136, 0.3); }
        50% { transform: scale(1.05); opacity: 0.85; box-shadow: 0 0 25px rgba(13, 148, 136, 0.5); }
        100% { transform: scale(0.95); opacity: 0.6; box-shadow: 0 0 15px rgba(13, 148, 136, 0.3); }
    }
    
    .empty-state .sub {
        font-size: 0.9rem;
        opacity: 0.7;
        margin-top: 0.5rem;
    }
    
    .anim-wrapper {
        animation: slide-in 0.4s ease-out both;
    }
</style>
