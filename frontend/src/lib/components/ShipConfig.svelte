<script lang="ts">
    import { ship, updateShip, quantumDrive, updateQuantumDrive } from '../stores/route.ts';
    import { Settings2 } from 'lucide-svelte';

    // Ship Search
    let shipSearchText = $ship?.Name || "Cutlass Black";
    let isSearchingShip = false;
    let shipResults = [];
    let shipSearchTimeout;

    function searchShip() {
        clearTimeout(shipSearchTimeout);
        if (!shipSearchText || shipSearchText.length < 2) {
            shipResults = [];
            isSearchingShip = false;
            return;
        }

        isSearchingShip = true;
        shipSearchTimeout = setTimeout(async () => {
            try {
                const url = `https://api.star-citizen.wiki/api/vehicles?sort=-cargo_capacity&filter[name]=${encodeURIComponent(shipSearchText)}`;
                const response = await fetch(url);
                const data = await response.json();
                shipResults = data.data || [];
            } catch (err) {
                console.error("Failed to fetch Ships", err);
                shipResults = [];
            } finally {
                isSearchingShip = false;
            }
        }, 500);
    }

    function selectShip(vehicle) {
        shipSearchText = vehicle.name;
        shipResults = [];
        
        const stats = {
            Name: vehicle.name,
            Mass: vehicle.mass || 242177.0,
            CargoCapacity: vehicle.cargo_capacity || 0,
            MaxSpeed: vehicle.speed?.max || 1150.0,
            MainThrust: vehicle.propulsion?.thrust_capacity?.main || 18830926.0
        };
        updateShip(stats);
    }

    // QD Search
    let qdSearchText = $quantumDrive?.Name || "Hemera";
    let isSearchingQD = false;
    let qdResults = [];
    let searchTimeout;

    function searchQD() {
        clearTimeout(searchTimeout);
        if (!qdSearchText || qdSearchText.length < 2) {
            qdResults = [];
            isSearchingQD = false;
            return;
        }

        isSearchingQD = true;
        searchTimeout = setTimeout(async () => {
            try {
                const url = `https://api.star-citizen.wiki/api/vehicle-items?filter[type]=QuantumDrive&filter[name]=${encodeURIComponent(qdSearchText)}`;
                const response = await fetch(url);
                const data = await response.json();
                qdResults = data.data || [];
            } catch (err) {
                console.error("Failed to fetch Quantum Drives", err);
                qdResults = [];
            } finally {
                isSearchingQD = false;
            }
        }, 500);
    }

    function selectQD(drive) {
        qdSearchText = drive.name;
        qdResults = [];
        
        const qt = drive.quantum_drive;
        const normal = qt.modes?.find(m => m.type === "normal_jump") || qt.standard_jump;
        const spline = qt.modes?.find(m => m.type === "spline_jump") || qt.spline_jump;
        
        const stats = {
            Name: drive.name,
            Standard: {
                DriveSpeed: normal.drive_speed,
                StageOneAccel: normal.stage_one_accel_rate,
                StageTwoAccel: normal.stage_two_accel_rate,
                SpoolUpTime: normal.spool_up_time,
                CooldownTime: normal.cooldown_time
            },
            Spline: {
                DriveSpeed: spline.drive_speed,
                StageOneAccel: spline.stage_one_accel_rate,
                StageTwoAccel: spline.stage_two_accel_rate,
                SpoolUpTime: spline.spool_up_time,
                CooldownTime: spline.cooldown_time
            }
        };
        updateQuantumDrive(stats);
    }
</script>

<div class="ship-config glass-panel">
    <div class="header">
        <Settings2 size={20} class="icon" />
        <h2>Ship Configuration</h2>
    </div>
    <div class="config-content">
        <!-- Ship Search -->
        <div class="input-group">
            <label for="ship-input">Ship Hull</label>
            <div class="qd-relative-container">
                <div class="input-wrapper qd-wrapper">
                    <input 
                        id="ship-input" 
                        type="text" 
                        class="text-input" 
                        placeholder="Search Ship..." 
                        bind:value={shipSearchText} 
                        on:input={searchShip} 
                    />
                </div>
                {#if isSearchingShip}
                    <div class="qd-dropdown"><div class="qd-item">Searching...</div></div>
                {:else if shipResults.length > 0}
                    <div class="qd-dropdown">
                        {#each shipResults as vehicle}
                            <!-- svelte-ignore a11y-click-events-have-key-events -->
                            <div class="qd-item" on:click={() => selectShip(vehicle)}>
                                {vehicle.name} <span class="qd-size">{vehicle.cargo_capacity || 0} SCU</span>
                            </div>
                        {/each}
                    </div>
                {/if}
            </div>
        </div>

        <!-- QD Search -->
        <div class="input-group">
            <label for="qd-input">Quantum Drive</label>
            <div class="qd-relative-container">
                <div class="input-wrapper qd-wrapper">
                    <input 
                        id="qd-input" 
                        type="text" 
                        class="text-input" 
                        placeholder="Search Drive..." 
                        bind:value={qdSearchText} 
                        on:input={searchQD} 
                    />
                </div>
                {#if isSearchingQD}
                    <div class="qd-dropdown"><div class="qd-item">Searching...</div></div>
                {:else if qdResults.length > 0}
                    <div class="qd-dropdown">
                        {#each qdResults as drive}
                            <!-- svelte-ignore a11y-click-events-have-key-events -->
                            <div class="qd-item" on:click={() => selectQD(drive)}>
                                {drive.name} <span class="qd-size">Size {drive.size}</span>
                            </div>
                        {/each}
                    </div>
                {/if}
            </div>
        </div>
    </div>
</div>

<style>
    .ship-config {
        padding: 1.5rem;
    }
    
    .header {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        margin-bottom: 1rem;
        border-bottom: 1px solid var(--bg-panel-border);
        padding-bottom: 0.5rem;
    }
    
    .header h2 {
        font-size: 1.2rem;
        color: var(--accent-cyan);
        margin: 0;
    }

    .icon {
        color: var(--accent-cyan);
    }
    
    .config-content {
        display: flex;
        flex-wrap: wrap;
        gap: 1.5rem;
    }
    
    .input-group {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
    }
    
    label {
        font-size: 0.85rem;
        color: var(--text-muted);
        text-transform: uppercase;
    }
    
    .input-wrapper {
        display: flex;
        align-items: center;
        background: rgba(0,0,0,0.3);
        border: 1px solid var(--bg-panel-border);
        border-radius: 4px;
        padding: 0.25rem 0.5rem;
        transition: var(--transition);
    }
    
    .input-wrapper:focus-within {
        border-color: var(--accent-cyan);
        box-shadow: 0 0 8px var(--accent-cyan-dim);
    }
    
    input {
        background: transparent;
        border: none;
        color: var(--text-main);
        font-family: var(--font-heading);
        font-size: 1.1rem;
        font-weight: 600;
        width: 60px;
        outline: none;
        -moz-appearance: textfield;
    }
    
    input::-webkit-outer-spin-button,
    input::-webkit-inner-spin-button {
        -webkit-appearance: none;
        margin: 0;
    }
    
    .unit {
        color: var(--accent-teal);
        font-weight: 600;
        font-size: 0.9rem;
    }

    .text-input {
        width: 150px;
        font-size: 0.95rem;
        font-weight: 500;
    }
    
    .qd-relative-container {
        position: relative;
        width: 170px;
    }
    
    .qd-wrapper {
        width: 100%;
    }

    .qd-dropdown {
        width: 100%;
        background: var(--bg-panel);
        border: 1px solid var(--bg-panel-border);
        border-radius: 4px;
        margin-top: 4px;
        z-index: 10;
        max-height: 150px;
        overflow-y: auto;
    }

    .qd-item {
        padding: 0.5rem;
        cursor: pointer;
        display: flex;
        justify-content: space-between;
        align-items: center;
        font-size: 0.9rem;
        color: var(--text-main);
        transition: var(--transition);
    }

    .qd-item:hover {
        background: rgba(0, 255, 255, 0.1);
        color: var(--accent-cyan);
    }

    .qd-size {
        font-size: 0.75rem;
        color: var(--text-muted);
    }
</style>
