<script lang="ts">
    import { Package, Truck, CheckCircle2, CircleDashed } from 'lucide-svelte';
    import { hoveredMissionIds } from '../stores/missions.ts';

    export let mission;

    $: isHaul = mission.missionType?.Case !== 'Courier';
    $: iconColor = isHaul ? 'var(--accent-warning)' : 'var(--accent-cyan)';
    $: totalCount = mission.objectives.length;
    $: displayedCompletedCount = (trigger, mission.objectives.filter(o => delayedCompleted.has(o.objectiveId)).length);
    $: progress = (trigger, totalCount > 0 ? (displayedCompletedCount / totalCount) * 100 : 0);
    
    let handledObjectives = new Set();
    let recentCompletions = new Set();
    let grayObjectives = new Set();
    let delayedCompleted = new Set();
    let trigger = 0;
    let firstRun = true;
    
    $: {
        if (mission && mission.objectives) {
            mission.objectives.forEach(obj => {
                if (obj.status?.Case === 'Completed') {
                    if (!handledObjectives.has(obj.objectiveId)) {
                        handledObjectives.add(obj.objectiveId);
                        
                        if (firstRun) {
                            delayedCompleted.add(obj.objectiveId);
                            grayObjectives.add(obj.objectiveId);
                        } else {
                            setTimeout(() => {
                                delayedCompleted.add(obj.objectiveId);
                                recentCompletions.add(obj.objectiveId);
                                trigger += 1;
                                setTimeout(() => {
                                    recentCompletions.delete(obj.objectiveId);
                                    grayObjectives.add(obj.objectiveId);
                                    trigger += 1;
                                }, 1500);
                            }, 2000);
                        }
                    }
                } else {
                    delayedCompleted.delete(obj.objectiveId);
                    grayObjectives.delete(obj.objectiveId);
                }
            });
            firstRun = false;
        }
    }

    $: sortedObjectives = (trigger, [...mission.objectives].sort((a, b) => {
        const getRank = (type) => {
            if (type?.Case === 'Nav') return 0;
            if (type?.Case === 'Pickup') return 1;
            if (type?.Case === 'Dropoff') return 2;
            return 3;
        };
        return getRank(a.type) - getRank(b.type);
    }));
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div class="mission-card glass-panel" 
     class:glitch-abort={mission.status?.Case === 'Abandoned' || mission.status?.Case === 'Failed'} 
     class:completed={mission.status?.Case === 'Completed'}
     class:highlighted={$hoveredMissionIds.has(mission.missionId)}
     on:mouseenter={() => hoveredMissionIds.set(new Set([mission.missionId]))}
     on:mouseleave={() => hoveredMissionIds.set(new Set())}>
    <div class="header">
        <div class="title-group">
            <div class="icon-bg" style="color: {iconColor}; border-color: {iconColor}40; background: {iconColor}10;">
                {#if isHaul}
                    <Truck size={18} />
                {:else}
                    <Package size={18} />
                {/if}
            </div>
            <h3>{mission.title}</h3>
        </div>
        <div class="status-badge {mission.status?.Case?.toLowerCase()}" class:completed={mission.status?.Case === 'Completed'}>
            {mission.status?.Case === 'Completed' ? 'COMPLETED' : (mission.status?.Case === 'Abandoned' ? 'ABANDONED' : (mission.status?.Case === 'Failed' ? 'FAILED' : 'ACTIVE'))}
        </div>
    </div>
    
    <div class="details">
        <span class="detail-label">{mission.generatorName}</span>
        {#if isHaul}
            {@const scuTotal = mission.objectives.reduce((acc, o) => acc + (o.scuAmount || 0), 0)}
            <span class="detail-value scu-tag">{scuTotal} SCU</span>
        {/if}
    </div>

    <div class="progress-container">
        <div class="progress-info">
            <span class="progress-text">Objectives</span>
            <span class="progress-numbers">{displayedCompletedCount} / {totalCount}</span>
        </div>
        <div class="progress-bar">
            <div class="progress-fill" style="width: {progress}%"></div>
        </div>
    </div>
    
    <div class="objectives-list">
        {#each sortedObjectives as obj}
            <div class="objective" class:done={grayObjectives.has(obj.objectiveId)} class:flash-obj={recentCompletions.has(obj.objectiveId)}>
                {#if delayedCompleted.has(obj.objectiveId)}
                    <CheckCircle2 size={14} class="obj-icon done" />
                {:else}
                    <CircleDashed size={14} class="obj-icon" />
                {/if}
                <span class="obj-type">{obj.type?.Case === 'Pickup' ? 'PICKUP' : (obj.type?.Case === 'Dropoff' ? 'DROPOFF' : 'NAV')}</span>
                <span class="obj-loc">{obj.destinationName || obj.resolvedLocation?.name || 'Unknown Location'}</span>
            </div>
        {/each}
    </div>
</div>

<style>
    .mission-card {
        padding: 1rem;
        transition: var(--transition);
        margin-bottom: 1rem;
        position: relative;
        overflow: hidden;
    }
    
    .mission-card:hover, .mission-card.highlighted {
        background: var(--bg-panel-hover);
        transform: translateX(4px);
        border-color: var(--accent-cyan-dim);
    }
    
    .mission-card.highlighted {
        box-shadow: 0 0 15px rgba(0, 255, 255, 0.2);
    }
    
    .mission-card.completed {
        border-color: rgba(74, 222, 128, 0.4);
        box-shadow: 0 0 15px rgba(74, 222, 128, 0.1);
    }
    
    .header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        gap: 1rem;
        margin-bottom: 0.75rem;
    }
    
    .title-group {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        flex: 1;
        min-width: 0;
    }
    
    .icon-bg {
        padding: 0.4rem;
        border-radius: 6px;
        border: 1px solid;
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
    }
    
    h3 {
        font-size: 1.1rem;
        color: var(--text-main);
        margin: 0;
        line-height: 1.2;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
    }
    
    .status-badge {
        font-size: 0.7rem;
        font-weight: 700;
        padding: 0.2rem 0.5rem;
        border-radius: 4px;
        background: rgba(0, 255, 255, 0.1);
        color: var(--accent-cyan);
        border: 1px solid var(--accent-cyan-dim);
        flex-shrink: 0;
        white-space: nowrap;
    }
    
    .status-badge.completed {
        background: rgba(74, 222, 128, 0.1);
        color: var(--accent-green);
        border-color: rgba(74, 222, 128, 0.4);
    }
    
    .status-badge.abandoned, .status-badge.failed {
        background: rgba(239, 68, 68, 0.1);
        color: rgba(239, 68, 68, 1);
        border-color: rgba(239, 68, 68, 0.3);
    }
    
    .details {
        display: flex;
        justify-content: space-between;
        margin-bottom: 1rem;
        font-size: 0.85rem;
    }
    
    .detail-label {
        color: var(--text-muted);
    }
    
    .scu-tag {
        background: rgba(245, 158, 11, 0.15);
        color: var(--accent-warning);
        padding: 0.1rem 0.4rem;
        border-radius: 3px;
        border: 1px solid rgba(245, 158, 11, 0.3);
        font-weight: 600;
    }
    
    .progress-container {
        margin-bottom: 1rem;
    }
    
    .progress-info {
        display: flex;
        justify-content: space-between;
        font-size: 0.8rem;
        margin-bottom: 0.25rem;
    }
    
    .progress-text { color: var(--text-muted); }
    .progress-numbers { color: var(--accent-teal); font-weight: 600; }
    
    .progress-bar {
        height: 4px;
        background: rgba(0, 0, 0, 0.5);
        border-radius: 2px;
        overflow: hidden;
    }
    
    .progress-fill {
        height: 100%;
        background: var(--accent-cyan);
        transition: width 0.5s ease-out;
    }
    
    .objectives-list {
        display: flex;
        flex-direction: column;
        gap: 0.4rem;
        font-size: 0.85rem;
    }
    
    .objective {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        color: var(--text-muted);
        transition: color 0.5s ease;
    }
    
    .objective.done {
        color: var(--text-main);
        opacity: 0.7;
    }
    
    .objective.flash-obj {
        animation: flash-obj-anim 1s ease-out forwards;
    }
    
    @keyframes flash-obj-anim {
        0% { color: var(--accent-green); text-shadow: 0 0 8px var(--accent-green); opacity: 1; }
        100% { color: var(--text-main); text-shadow: none; opacity: 0.7; }
    }
    
    .obj-icon { color: var(--text-muted); }
    .obj-icon.done { color: var(--accent-green); }
    
    .obj-type {
        font-weight: 600;
        font-size: 0.75rem;
        letter-spacing: 0.5px;
    }
    
    .obj-loc {
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .glitch-abort {
        border-color: rgba(239, 68, 68, 0.8) !important;
        background: rgba(239, 68, 68, 0.1) !important;
    }
</style>
