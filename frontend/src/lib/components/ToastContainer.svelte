<script lang="ts">
    import { notifications, dismissNotification } from '../stores/notifications';
    import { fly, fade } from 'svelte/transition';
</script>

<div class="toast-container" aria-live="polite">
    {#each $notifications as toast (toast.id)}
        <div 
            class="toast-item industrial-warning"
            in:fly={{ y: -30, duration: 350 }}
            out:fade={{ duration: 200 }}
        >
            <!-- Left Hazard Strip -->
            <div class="hazard-stripe"></div>

            <div class="toast-body">
                <div class="toast-header">
                    <div class="title-wrap">
                        <svg class="warning-svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
                            <line x1="12" y1="9" x2="12" y2="13"/>
                            <line x1="12" y1="17" x2="12.01" y2="17"/>
                        </svg>
                        <h3 class="toast-title">{toast.title}</h3>
                    </div>
                    <button 
                        class="close-btn" 
                        on:click={() => dismissNotification(toast.id)}
                        aria-label="Dismiss notification"
                        title="Dismiss"
                    >
                        ✕
                    </button>
                </div>

                {#if toast.missionTitle}
                    <div class="mission-tag">
                        <span class="tag-prefix">TARGET //</span>
                        <span class="tag-text">{toast.missionTitle}</span>
                    </div>
                {/if}

                <p class="toast-message">{toast.message}</p>
            </div>

            <!-- Auto-dismiss Progress Bar -->
            {#if toast.duration > 0}
                <div class="progress-track">
                    <div 
                        class="progress-bar" 
                        style="animation-duration: {toast.duration}ms;"
                    ></div>
                </div>
            {/if}
        </div>
    {/each}
</div>

<style>
    .toast-container {
        position: fixed;
        top: 24px;
        left: 50%;
        transform: translateX(-50%);
        z-index: 10000;
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 16px;
        max-width: 680px;
        width: calc(100vw - 48px);
        pointer-events: none;
    }

    .toast-item {
        pointer-events: auto;
        position: relative;
        width: 100%;
        display: flex;
        flex-direction: column;
        background: linear-gradient(135deg, rgba(24, 20, 12, 0.97) 0%, rgba(14, 15, 20, 0.97) 100%);
        backdrop-filter: blur(20px);
        -webkit-backdrop-filter: blur(20px);
        border: 1.5px solid rgba(245, 158, 11, 0.7);
        border-radius: 8px;
        box-shadow: 
            0 0 35px rgba(245, 158, 11, 0.3),
            0 16px 45px rgba(0, 0, 0, 0.8),
            inset 0 1px 0 rgba(255, 255, 255, 0.15);
        overflow: hidden;
        transition: transform 0.2s ease, box-shadow 0.2s ease;
    }

    .toast-item:hover {
        border-color: rgba(251, 191, 36, 0.95);
        box-shadow: 
            0 0 45px rgba(245, 158, 11, 0.45),
            0 18px 50px rgba(0, 0, 0, 0.85),
            inset 0 1px 0 rgba(255, 255, 255, 0.2);
    }

    /* Vertical Industrial Hazard Stripe on Left Edge */
    .hazard-stripe {
        position: absolute;
        top: 0;
        left: 0;
        bottom: 0;
        width: 8px;
        background: repeating-linear-gradient(
            135deg,
            #f59e0b,
            #f59e0b 10px,
            #0a0b0e 10px,
            #0a0b0e 20px
        );
        border-right: 1px solid rgba(245, 158, 11, 0.5);
    }

    .toast-body {
        padding: 18px 24px 18px 28px;
        display: flex;
        flex-direction: column;
        gap: 10px;
    }

    .toast-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 16px;
    }

    .title-wrap {
        display: flex;
        align-items: center;
        gap: 10px;
    }

    .warning-svg {
        width: 24px;
        height: 24px;
        color: #fbbf24;
        filter: drop-shadow(0 0 8px rgba(245, 158, 11, 0.8));
        flex-shrink: 0;
    }

    .toast-title {
        font-family: var(--font-heading, 'Rajdhani', sans-serif);
        font-size: 1.15rem;
        font-weight: 700;
        color: #fbbf24;
        text-transform: uppercase;
        letter-spacing: 2px;
        text-shadow: 0 0 10px rgba(245, 158, 11, 0.6);
        margin: 0;
    }

    .close-btn {
        background: rgba(245, 158, 11, 0.12);
        border: 1px solid rgba(245, 158, 11, 0.4);
        color: #f59e0b;
        border-radius: 4px;
        width: 28px;
        height: 28px;
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: pointer;
        font-size: 0.95rem;
        font-weight: bold;
        transition: all 0.2s ease;
        padding: 0;
        flex-shrink: 0;
    }

    .close-btn:hover {
        background: #f59e0b;
        color: #0b0c10;
        border-color: #fbbf24;
        box-shadow: 0 0 12px rgba(245, 158, 11, 0.7);
    }

    .mission-tag {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        background: rgba(245, 158, 11, 0.14);
        border: 1px solid rgba(245, 158, 11, 0.3);
        padding: 4px 10px;
        border-radius: 4px;
        width: fit-content;
        max-width: 100%;
    }

    .tag-prefix {
        font-family: var(--font-heading, 'Rajdhani', sans-serif);
        font-size: 0.8rem;
        font-weight: 700;
        color: #f59e0b;
        letter-spacing: 1px;
    }

    .tag-text {
        font-family: var(--font-heading, 'Rajdhani', sans-serif);
        font-size: 0.9rem;
        font-weight: 600;
        color: #fde047;
        letter-spacing: 0.8px;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .toast-message {
        font-family: var(--font-body, 'Exo 2', sans-serif);
        font-size: 1.05rem;
        line-height: 1.5;
        color: #fef08a;
        margin: 0;
        word-break: break-word;
    }

    /* Progress bar track & animation */
    .progress-track {
        height: 4.5px;
        width: 100%;
        background: rgba(245, 158, 11, 0.2);
        overflow: hidden;
    }

    .progress-bar {
        height: 100%;
        width: 100%;
        background: linear-gradient(90deg, #d97706, #f59e0b, #fde047);
        box-shadow: 0 0 10px rgba(245, 158, 11, 0.9);
        animation: shrink-progress linear forwards;
    }

    @keyframes shrink-progress {
        from {
            width: 100%;
        }
        to {
            width: 0%;
        }
    }
</style>
