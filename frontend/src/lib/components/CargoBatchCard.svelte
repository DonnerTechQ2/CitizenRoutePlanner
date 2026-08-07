<script lang="ts">
    import { Boxes, ArrowRight, CheckCircle2, PackageCheck, Truck, Package } from 'lucide-svelte';
    import { hoveredMissionIds } from '../stores/missions.ts';

    export let mission: any;
    export let index: number = 0;

    $: isHighlighted = $hoveredMissionIds.has(mission.missionId);

    interface CargoItem {
        cargoType: string;
        scuAmount: number;
        fromLoc: string;
        toLoc: string;
        status: 'AWAITING PICKUP' | 'IN TRANSIT' | 'DELIVERED';
    }

    // Extract itemized cargo entries with individual SCU amounts and routes
    $: cargoItems = (() => {
        if (!mission || !mission.objectives) return [];

        const pickups = mission.objectives.filter((o: any) => o.type?.Case === 'Pickup');
        const dropoffs = mission.objectives.filter((o: any) => o.type?.Case === 'Dropoff');

        const items: CargoItem[] = [];

        if (dropoffs.length > 0) {
            dropoffs.forEach((d: any) => {
                const matchedPickup = pickups.find((p: any) => 
                    (d.pairedObjectiveId && p.objectiveId === d.pairedObjectiveId) ||
                    (p.pairedObjectiveId && p.pairedObjectiveId === d.objectiveId)
                ) || pickups[0];

                const cType = d.cargoType || matchedPickup?.cargoType || 'Cargo';
                const scu = d.scuAmount || matchedPickup?.scuAmount || 0;
                const fromLoc = matchedPickup?.destinationName || matchedPickup?.resolvedLocation?.name || 'Origin';
                const toLoc = d.destinationName || d.resolvedLocation?.name || 'Destination';

                let itemStatus: 'AWAITING PICKUP' | 'IN TRANSIT' | 'DELIVERED' = 'AWAITING PICKUP';
                if (d.status?.Case === 'Completed') {
                    itemStatus = 'DELIVERED';
                } else if (matchedPickup?.status?.Case === 'Completed' || pickups.some((p: any) => p.status?.Case === 'Completed')) {
                    itemStatus = 'IN TRANSIT';
                }

                items.push({
                    cargoType: cType,
                    scuAmount: scu,
                    fromLoc,
                    toLoc,
                    status: itemStatus
                });
            });
        } else if (pickups.length > 0) {
            pickups.forEach((p: any) => {
                const cType = p.cargoType || 'Cargo';
                const scu = p.scuAmount || 0;
                const fromLoc = p.destinationName || p.resolvedLocation?.name || 'Origin';
                const toLoc = 'Destination';
                const itemStatus = p.status?.Case === 'Completed' ? 'IN TRANSIT' : 'AWAITING PICKUP';

                items.push({
                    cargoType: cType,
                    scuAmount: scu,
                    fromLoc,
                    toLoc,
                    status: itemStatus
                });
            });
        }

        // Aggregate identical items (same cargoType, fromLoc, toLoc, and status) to avoid duplicate rows while preserving individual SCU amounts
        const groupedMap = new Map<string, CargoItem>();
        items.forEach(item => {
            const key = `${item.cargoType.toLowerCase()}|${item.fromLoc.toLowerCase()}|${item.toLoc.toLowerCase()}|${item.status}`;
            if (groupedMap.has(key)) {
                const existing = groupedMap.get(key)!;
                existing.scuAmount += item.scuAmount;
            } else {
                groupedMap.set(key, { ...item });
            }
        });

        return Array.from(groupedMap.values());
    })();

    // Total SCU for the batch
    $: totalBatchScu = cargoItems.reduce((sum, item) => sum + item.scuAmount, 0);

    // Overall Batch Status
    $: overallStatus = (() => {
        if (cargoItems.length === 0) return 'AWAITING PICKUP';
        if (cargoItems.every(i => i.status === 'DELIVERED')) return 'DELIVERED';
        if (cargoItems.some(i => i.status === 'IN TRANSIT' || i.status === 'DELIVERED')) return 'IN TRANSIT';
        return 'AWAITING PICKUP';
    })();

    function handleMouseEnter() {
        hoveredMissionIds.set(new Set([mission.missionId]));
    }

    function handleMouseLeave() {
        hoveredMissionIds.set(new Set());
    }
</script>

<!-- svelte-ignore a11y-no-static-element-interactions -->
<div 
    class="cargo-batch-card glass-panel"
    class:highlighted={isHighlighted}
    class:in-transit={overallStatus === 'IN TRANSIT'}
    class:delivered={overallStatus === 'DELIVERED'}
    on:mouseenter={handleMouseEnter}
    on:mouseleave={handleMouseLeave}
>
    <div class="batch-header">
        <div class="batch-tag">
            <Boxes size={14} />
            <span>BATCH #{index + 1}</span>
        </div>
        
        <div class="scu-badge">
            <span class="scu-number">{totalBatchScu}</span>
            <span class="scu-unit">SCU TOTAL</span>
        </div>

        <div class="status-pill {overallStatus.toLowerCase().replace(' ', '-')}">
            {#if overallStatus === 'DELIVERED'}
                <CheckCircle2 size={12} />
            {:else if overallStatus === 'IN TRANSIT'}
                <Truck size={12} />
            {:else}
                <PackageCheck size={12} />
            {/if}
            <span>{overallStatus}</span>
        </div>
    </div>

    <div class="mission-info">
        <h4 class="mission-title" title={mission.title}>{mission.title}</h4>
        <span class="generator-name">{mission.generatorName || 'Contractor'}</span>
    </div>

    <div class="items-breakdown">
        <div class="breakdown-header">CARGO BREAKDOWN ({cargoItems.length} {cargoItems.length === 1 ? 'ITEM' : 'ITEMS'})</div>
        {#each cargoItems as item}
            <div class="cargo-item-row" class:delivered={item.status === 'DELIVERED'} class:in-transit={item.status === 'IN TRANSIT'}>
                <div class="item-main">
                    <span class="item-name" title={item.cargoType}>{item.cargoType}</span>
                    <span class="item-scu">{item.scuAmount} SCU</span>
                </div>
                <div class="item-route">
                    <span class="loc-from" title={item.fromLoc}>{item.fromLoc}</span>
                    <ArrowRight size={10} class="arrow-icon" />
                    <span class="loc-to" title={item.toLoc}>{item.toLoc}</span>
                    <span class="item-status-tag {item.status.toLowerCase().replace(' ', '-')}">{item.status}</span>
                </div>
            </div>
        {/each}
    </div>
</div>

<style>
    .cargo-batch-card {
        padding: 0.9rem;
        transition: var(--transition);
        margin-bottom: 0.85rem;
        position: relative;
        overflow: hidden;
        border-left: 3px solid var(--accent-teal);
        display: flex;
        flex-direction: column;
        gap: 0.65rem;
    }

    .cargo-batch-card:hover, .cargo-batch-card.highlighted {
        background: var(--bg-panel-hover);
        transform: translateX(4px);
        border-color: var(--accent-cyan);
        box-shadow: 0 0 15px rgba(0, 255, 255, 0.2);
    }

    .cargo-batch-card.in-transit {
        border-left-color: var(--accent-warning);
    }

    .cargo-batch-card.delivered {
        border-left-color: var(--accent-green);
        opacity: 0.75;
    }

    .batch-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 0.5rem;
        flex-wrap: wrap;
    }

    .batch-tag {
        display: flex;
        align-items: center;
        gap: 0.35rem;
        font-family: var(--font-heading);
        font-size: 0.8rem;
        font-weight: 700;
        letter-spacing: 0.5px;
        color: var(--accent-cyan);
        background: rgba(0, 255, 255, 0.1);
        padding: 0.15rem 0.45rem;
        border-radius: 4px;
        border: 1px solid rgba(0, 255, 255, 0.25);
        white-space: nowrap;
        flex-shrink: 0;
    }

    .scu-badge {
        display: flex;
        align-items: baseline;
        gap: 0.25rem;
        background: rgba(245, 158, 11, 0.15);
        color: var(--accent-warning);
        border: 1px solid rgba(245, 158, 11, 0.35);
        padding: 0.15rem 0.45rem;
        border-radius: 4px;
        font-family: var(--font-heading);
        font-weight: 700;
        white-space: nowrap;
        flex-shrink: 0;
    }

    .scu-number {
        font-size: 0.95rem;
        line-height: 1;
        white-space: nowrap;
    }

    .scu-unit {
        font-size: 0.65rem;
        letter-spacing: 0.5px;
        white-space: nowrap;
    }

    .status-pill {
        display: flex;
        align-items: center;
        gap: 0.3rem;
        font-size: 0.65rem;
        font-weight: 700;
        letter-spacing: 0.5px;
        padding: 0.15rem 0.45rem;
        border-radius: 4px;
        background: rgba(255, 255, 255, 0.05);
        color: var(--text-muted);
        border: 1px solid rgba(255, 255, 255, 0.1);
        white-space: nowrap;
        flex-shrink: 0;
        margin-left: auto;
    }

    .status-pill.awaiting-pickup {
        background: rgba(13, 148, 136, 0.15);
        color: var(--accent-teal);
        border-color: rgba(13, 148, 136, 0.3);
    }

    .status-pill.in-transit {
        background: rgba(245, 158, 11, 0.15);
        color: var(--accent-warning);
        border-color: rgba(245, 158, 11, 0.3);
    }

    .status-pill.delivered {
        background: rgba(74, 222, 128, 0.15);
        color: var(--accent-green);
        border-color: rgba(74, 222, 128, 0.3);
    }

    .mission-info {
        display: flex;
        flex-direction: column;
        gap: 0.1rem;
        min-width: 0;
    }

    .mission-title {
        font-size: 0.95rem;
        color: var(--text-main);
        margin: 0;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .generator-name {
        font-size: 0.75rem;
        color: var(--text-muted);
    }

    .items-breakdown {
        display: flex;
        flex-direction: column;
        gap: 0.4rem;
        background: rgba(0, 0, 0, 0.3);
        padding: 0.5rem;
        border-radius: 6px;
        border: 1px solid rgba(255, 255, 255, 0.05);
    }

    .breakdown-header {
        font-size: 0.65rem;
        font-weight: 700;
        letter-spacing: 0.8px;
        color: var(--text-muted);
        opacity: 0.8;
        margin-bottom: 0.1rem;
    }

    .cargo-item-row {
        display: flex;
        flex-direction: column;
        gap: 0.2rem;
        background: rgba(255, 255, 255, 0.03);
        padding: 0.35rem 0.5rem;
        border-radius: 4px;
        border-left: 2px solid rgba(0, 255, 255, 0.3);
    }

    .cargo-item-row.in-transit {
        border-left-color: var(--accent-warning);
    }

    .cargo-item-row.delivered {
        border-left-color: var(--accent-green);
        opacity: 0.7;
    }

    .item-main {
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 0.5rem;
    }

    .item-name {
        font-size: 0.85rem;
        font-weight: 600;
        color: var(--accent-cyan);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        flex: 1;
    }

    .item-scu {
        font-size: 0.8rem;
        font-weight: 700;
        color: var(--accent-warning);
        background: rgba(245, 158, 11, 0.1);
        padding: 0.05rem 0.35rem;
        border-radius: 3px;
        border: 1px solid rgba(245, 158, 11, 0.25);
        white-space: nowrap;
    }

    .item-route {
        display: flex;
        align-items: center;
        gap: 0.35rem;
        font-size: 0.72rem;
        color: var(--text-muted);
        min-width: 0;
    }

    .loc-from, .loc-to {
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        max-width: 120px;
    }

    :global(.arrow-icon) {
        color: var(--accent-teal);
        flex-shrink: 0;
    }

    .item-status-tag {
        margin-left: auto;
        font-size: 0.6rem;
        font-weight: 700;
        padding: 0.05rem 0.3rem;
        border-radius: 3px;
        text-transform: uppercase;
        flex-shrink: 0;
    }

    .item-status-tag.awaiting-pickup {
        color: var(--accent-teal);
        background: rgba(13, 148, 136, 0.15);
    }

    .item-status-tag.in-transit {
        color: var(--accent-warning);
        background: rgba(245, 158, 11, 0.15);
    }

    .item-status-tag.delivered {
        color: var(--accent-green);
        background: rgba(74, 222, 128, 0.15);
    }
</style>
