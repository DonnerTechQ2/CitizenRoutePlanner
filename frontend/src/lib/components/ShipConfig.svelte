<script lang="ts">
    import { shipCapacity, updateShipCapacity, shipSpeedModifier, updateShipSpeedModifier } from '../stores/route.ts';
    import { Settings2 } from 'lucide-svelte';

    let scuInput = $shipCapacity;
    let speedInput = $shipSpeedModifier;

    function handleSave() {
        if (scuInput >= 0) {
            updateShipCapacity(scuInput);
        }
    }

    function handleSpeedSave() {
        if (speedInput > 0) {
            updateShipSpeedModifier(speedInput);
        }
    }
</script>

<div class="ship-config glass-panel">
    <div class="header">
        <Settings2 size={20} class="icon" />
        <h2>Ship Configuration</h2>
    </div>
    <div class="config-content">
        <div class="input-group">
            <label for="scu-input">Cargo Capacity (SCU)</label>
            <div class="input-wrapper">
                <input 
                    id="scu-input" 
                    type="number" 
                    min="0" 
                    bind:value={scuInput} 
                    on:change={handleSave}
                />
                <span class="unit">SCU</span>
            </div>
        </div>
        
        <div class="input-group">
            <label for="speed-input">Ship Speed Modifier</label>
            <div class="input-wrapper">
                <input 
                    id="speed-input" 
                    type="number" 
                    min="0.1"
                    step="0.1"
                    bind:value={speedInput} 
                    on:change={handleSpeedSave}
                />
                <span class="unit">x</span>
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
        gap: 2rem;
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
</style>
