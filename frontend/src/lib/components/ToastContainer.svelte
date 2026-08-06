<script lang="ts">
    import { notifications, dismissNotification } from '../stores/notifications';
    import { fly, fade } from 'svelte/transition';
</script>

<div class="toast-container" aria-live="polite">
    {#each $notifications as toast (toast.id)}
        <div 
            class="toast-item industrial-warning"
            in:fly={{ y: -20, duration: 300 }}
            out:fade={{ duration: 200 }}
        >
            <!-- Left Hazard Strip -->
            <div class="hazard-stripe"></div>

            <div class="toast-body">
                <div class="toast-header">
                    <div class="title-wrap">
                        <span class="warning-badge">⚠️</span>
                        <h4 class="toast-title">{toast.title}</h4>
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
                        <span class="tag-icon">🎯</span>
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
        right: 24px;
        z-index: 10000;
        display: flex;
        flex-direction: column;
        gap: 14px;
        max-width: 420px;
        width: calc(100vw - 48px);
        pointer-events: none;
    }

    .toast-item {
        pointer-events: auto;
        position: relative;
        display: flex;
        flex-direction: column;
        background: linear-gradient(135deg, rgba(22, 19, 12, 0.96) 0%, rgba(13, 14, 18, 0.96) 100%);
        backdrop-filter: blur(16px);
        -webkit-backdrop-filter: blur(16px);
        border: 1px solid rgba(245, 158, 11, 0.6);
        border-radius: 6px;
        box-shadow: 
            0 0 25px rgba(245, 158, 11, 0.25),
            0 10px 30px rgba(0, 0, 0, 0.7),
            inset 0 1px 0 rgba(255, 255, 255, 0.1);
        overflow: hidden;
        transition: transform 0.2s ease, box-shadow 0.2s ease;
    }

    .toast-item:hover {
        border-color: rgba(251, 191, 36, 0.9);
        box-shadow: 
            0 0 35px rgba(245, 158, 11, 0.4),
            0 12px 35px rgba(0, 0, 0, 0.8),
            inset 0 1px 0 rgba(255, 255, 255, 0.15);
    }

    /* Vertical Industrial Hazard Stripe on Left Edge */
    .hazard-stripe {
        position: absolute;
        top: 0;
        left: 0;
        bottom: 0;
        width: 6px;
        background: repeating-linear-gradient(
            135deg,
            #f59e0b,
            #f59e0b 8px,
            #0b0c10 8px,
            #0b0c10 16px
        );
        border-right: 1px solid rgba(245, 158, 11, 0.4);
    }

    .toast-body {
        padding: 14px 16px 14px 20px;
        display: flex;
        flex-direction: column;
        gap: 8px;
    }

    .toast-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
    }

    .title-wrap {
        display: flex;
        align-items: center;
        gap: 8px;
    }

    .warning-badge {
        font-size: 1.1rem;
        line-height: 1;
        filter: drop-shadow(0 0 6px rgba(245, 158, 11, 0.8));
    }

    .toast-title {
        font-family: var(--font-heading, 'Rajdhani', sans-serif);
        font-size: 0.95rem;
        font-weight: 700;
        color: #fbbf24;
        text-transform: uppercase;
        letter-spacing: 1.2px;
        text-shadow: 0 0 8px rgba(245, 158, 11, 0.5);
        margin: 0;
    }

    .close-btn {
        background: rgba(245, 158, 11, 0.1);
        border: 1px solid rgba(245, 158, 11, 0.3);
        color: #f59e0b;
        border-radius: 4px;
        width: 24px;
        height: 24px;
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: pointer;
        font-size: 0.8rem;
        font-weight: bold;
        transition: all 0.2s ease;
        padding: 0;
        flex-shrink: 0;
    }

    .close-btn:hover {
        background: #f59e0b;
        color: #0b0c10;
        border-color: #fbbf24;
        box-shadow: 0 0 10px rgba(245, 158, 11, 0.6);
    }

    .mission-tag {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        background: rgba(245, 158, 11, 0.12);
        border: 1px solid rgba(245, 158, 11, 0.25);
        padding: 2px 8px;
        border-radius: 4px;
        width: fit-content;
        max-width: 100%;
    }

    .tag-icon {
        font-size: 0.75rem;
    }

    .tag-text {
        font-family: var(--font-heading, 'Rajdhani', sans-serif);
        font-size: 0.8rem;
        font-weight: 600;
        color: #fde047;
        letter-spacing: 0.5px;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
    }

    .toast-message {
        font-family: var(--font-body, 'Exo 2', sans-serif);
        font-size: 0.88rem;
        line-height: 1.45;
        color: #fef08a;
        margin: 0;
        word-break: break-word;
    }

    /* Progress bar track & animation */
    .progress-track {
        height: 3.5px;
        width: 100%;
        background: rgba(245, 158, 11, 0.15);
        overflow: hidden;
    }

    .progress-bar {
        height: 100%;
        width: 100%;
        background: linear-gradient(90deg, #d97706, #f59e0b, #fbbf24);
        box-shadow: 0 0 8px rgba(245, 158, 11, 0.8);
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
