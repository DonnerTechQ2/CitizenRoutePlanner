<script lang="ts">
    import { missions } from '../stores/missions.ts';
    import CargoBatchCard from './CargoBatchCard.svelte';
    import { Boxes, Layers, Package } from 'lucide-svelte';

    $: activeCargoMissions = Array.from($missions.values()).filter(m => {
        if (m.status?.Case !== 'Active') return false;
        if (m.missionType?.Case === 'Courier') return false;
        
        const pickupScu = (m.objectives || [])
            .filter((o: any) => o.type?.Case === 'Pickup')
            .reduce((acc: number, o: any) => acc + (o.scuAmount || 0), 0);
        const dropoffScu = (m.objectives || [])
            .filter((o: any) => o.type?.Case === 'Dropoff')
            .reduce((acc: number, o: any) => acc + (o.scuAmount || 0), 0);
        
        return Math.max(pickupScu, dropoffScu) > 0;
    });

    $: totalScuSum = activeCargoMissions.reduce((acc, m) => {
        const pickupScu = (m.objectives || [])
            .filter((o: any) => o.type?.Case === 'Pickup')
            .reduce((acc: number, o: any) => acc + (o.scuAmount || 0), 0);
        const dropoffScu = (m.objectives || [])
            .filter((o: any) => o.type?.Case === 'Dropoff')
            .reduce((acc: number, o: any) => acc + (o.scuAmount || 0), 0);
        return acc + Math.max(pickupScu, dropoffScu);
    }, 0);
</script>

<div class="cargo-panel">
    <div class="panel-header">
        <Boxes size={22} class="header-icon" />
        <h2>Cargo Batches</h2>
        <span class="count-badge" title="Total SCU: {totalScuSum}">{activeCargoMissions.length} ({totalScuSum} SCU)</span>
    </div>

    <div class="batches-list">
        {#if activeCargoMissions.length === 0}
            <div class="empty-state">
                <p>No active cargo batches.</p>
                <p class="sub">Accept haulage contracts to view segregated cargo groups.</p>
            </div>
        {:else}
            {#each activeCargoMissions as mission, index (mission.missionId)}
                <div class="anim-wrapper">
                    <CargoBatchCard {mission} {index} />
                </div>
            {/each}
        {/if}
    </div>
</div>

<style>
    .cargo-panel {
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
        color: var(--accent-warning);
        flex: 1;
        font-size: 1.25rem;
    }

    :global(.header-icon) {
        color: var(--accent-warning);
    }

    .count-badge {
        background: rgba(245, 158, 11, 0.2);
        color: var(--accent-warning);
        border: 1px solid rgba(245, 158, 11, 0.4);
        font-weight: 700;
        padding: 0.1rem 0.6rem;
        border-radius: 12px;
        font-size: 0.85rem;
        white-space: nowrap;
    }

    .batches-list {
        flex: 1;
        overflow-y: auto;
        scrollbar-gutter: stable;
        padding-right: 0.5rem;
        scroll-behavior: smooth;
    }

    .empty-state {
        text-align: center;
        padding: 3rem 1rem;
        color: var(--text-muted);
        background: rgba(0, 0, 0, 0.2);
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
