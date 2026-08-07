<script lang="ts">
    import { missions } from '../stores/missions.ts';
    import MissionCard from './MissionCard.svelte';
    import { ClipboardList } from 'lucide-svelte';

    let recentlyAborted = new Set();
    let recentlyCompleted = new Set();
    let handledAborts = new Set();
    let handledCompletions = new Set();
    let trigger = 0; // used to force reactivity

    // To track objective completion for scrolling
    let lastObjectiveStates = new Map();
    
    // Elements map for scrolling
    let missionElements = {};

    $: {
        if ($missions) {
            $missions.forEach(m => {
                // Aborted / Failed
                if (m.status?.Case === 'Abandoned' || m.status?.Case === 'Failed') {
                    if (!handledAborts.has(m.missionId)) {
                        handledAborts.add(m.missionId);
                        recentlyAborted.add(m.missionId);
                        trigger += 1;
                        
                        // Update status from store when a mission is aborting
                        const realMission = $missions.get(m.missionId);
                        if (realMission) {
                            m.status = realMission.status;
                        }
                        
                        setTimeout(() => {
                            recentlyAborted.delete(m.missionId);
                            trigger += 1; // trigger re-filter
                        }, 1500);
                    }
                }
                
                // Completed delay & scroll
                if (m.status?.Case === 'Completed') {
                    if (!handledCompletions.has(m.missionId)) {
                        handledCompletions.add(m.missionId);
                        recentlyCompleted.add(m.missionId);
                        trigger += 1;
                        
                        setTimeout(() => {
                            if (missionElements[m.missionId]) {
                                missionElements[m.missionId].scrollIntoView({ behavior: 'smooth', block: 'center' });
                            }
                        }, 50);

                        setTimeout(() => {
                            recentlyCompleted.delete(m.missionId);
                            trigger += 1;
                        }, 4000); // 4 seconds delay before sorting down
                    }
                }

                // Objective progression scroll
                const objStr = JSON.stringify(m.objectives);
                const lastObjStr = lastObjectiveStates.get(m.missionId);
                if (lastObjStr && lastObjStr !== objStr && m.status?.Case !== 'Completed') {
                     const oldCompleted = JSON.parse(lastObjStr).filter(o => o.status?.Case === 'Completed').length;
                     const newCompleted = m.objectives.filter(o => o.status?.Case === 'Completed').length;
                     
                     if (newCompleted > oldCompleted) {
                         setTimeout(() => {
                             if (missionElements[m.missionId]) {
                                 missionElements[m.missionId].scrollIntoView({ behavior: 'smooth', block: 'center' });
                             }
                         }, 50);
                     }
                }
                lastObjectiveStates.set(m.missionId, objStr);
            });
        }
    }

    $: activeCount = Array.from($missions.values()).filter(m => m.status?.Case === 'Active').length;

    $: missionsList = (trigger, Array.from($missions.values()))
        .sort((a, b) => {
            const getStatusRank = (mission) => {
                const s = mission.status;
                if (s?.Case === 'Active' || recentlyCompleted.has(mission.missionId) || recentlyAborted.has(mission.missionId)) return 0;
                if (s?.Case === 'Completed' && !recentlyCompleted.has(mission.missionId)) return 1;
                return 2; // aborted/failed
            };
            const rankA = getStatusRank(a);
            const rankB = getStatusRank(b);
            if (rankA !== rankB) return rankA - rankB;
            return new Date(b.acceptedAt).getTime() - new Date(a.acceptedAt).getTime();
        });
</script>

<div class="mission-panel">
    <div class="panel-header">
        <ClipboardList size={22} class="icon" />
        <h2>Active Contracts</h2>
        <span class="count-badge">{missionsList.length}</span>
    </div>

    <div class="missions-list">
        {#if missionsList.length === 0}
            <div class="empty-state">
                <p>No active missions.</p>
                <p class="sub">Accept contracts in your mobiGlas to see them here.</p>
            </div>
        {:else}
            {#each missionsList as mission (mission.missionId)}
                <div class="anim-wrapper" bind:this={missionElements[mission.missionId]}>
                    <MissionCard {mission} />
                </div>
            {/each}
        {/if}
    </div>
</div>

<style>
    .mission-panel {
        display: flex;
        flex-direction: column;
        flex: 1;
        min-height: 0;
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
        color: var(--accent-cyan);
        flex: 1;
    }
    
    .icon {
        color: var(--accent-cyan);
    }
    
    .count-badge {
        background: var(--accent-teal);
        color: #000;
        font-weight: 700;
        padding: 0.1rem 0.6rem;
        border-radius: 12px;
        font-size: 0.9rem;
    }
    
    .missions-list {
        flex: 1;
        overflow-y: auto;
        scrollbar-gutter: stable;
        padding-right: 0.5rem;
        /* smooth scrolling behavior */
        scroll-behavior: smooth;
    }
    
    .empty-state {
        text-align: center;
        padding: 3rem 1rem;
        color: var(--text-muted);
        background: rgba(0,0,0,0.2);
        border: 1px dashed var(--bg-panel-border);
        border-radius: 8px;
    }
    
    .empty-state .sub {
        font-size: 0.85rem;
        opacity: 0.7;
        margin-top: 0.5rem;
    }
    
    .anim-wrapper {
        animation: slide-in 0.3s ease-out forwards;
    }
</style>
