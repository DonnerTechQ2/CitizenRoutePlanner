import { cubicInOut } from 'svelte/easing';

export function morphOut(node, { duration = 800 }) {
    // We create a checkmark overlay and append it to the node
    const checkmark = document.createElement('div');
    checkmark.innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" class="lucide lucide-check"><path d="M20 6 9 17l-5-5"/></svg>`;
    checkmark.style.cssText = `
        position: absolute;
        top: 50%;
        left: 50%;
        transform: translate(-50%, -50%);
        color: white;
        opacity: 0;
        z-index: 10;
    `;
    node.style.position = 'relative';
    node.style.overflow = 'hidden';
    node.appendChild(checkmark);

    // Get original dimensions
    const width = node.offsetWidth;
    const height = node.offsetHeight;

    return {
        duration,
        css: (t, u) => {
            // t goes from 1 to 0
            let easeU = cubicInOut(u); // 0 to 1
            
            let scaleX = 1;
            let scaleY = 1;
            let radius = 8;
            let bg = 'rgba(13, 22, 30, 0.65)'; // default
            
            if (u < 0.4) {
                // Morph to circle (0 to 40% of time)
                let p = u / 0.4;
                scaleX = 1 - p * (1 - (height / width)); // squeeze width to match height
                radius = 8 + p * (height/2 - 8);
                bg = `rgba(${13 + p*(42-13)}, ${22 + p*(222-22)}, ${30 + p*(128-30)}, 1)`; // morph to #2ade80 (green)
            } else if (u < 0.8) {
                // Hold as circle (40% to 80% of time)
                scaleX = height / width;
                radius = height / 2;
                bg = '#2ade80';
            } else {
                // Shrink and disappear (80% to 100% of time)
                let p = (u - 0.8) / 0.2;
                scaleX = (height / width) * (1 - p);
                scaleY = 1 - p;
                radius = height / 2;
                bg = '#2ade80';
            }

            return `
                transform: scale(${scaleX}, ${scaleY});
                border-radius: ${radius}px;
                background: ${bg};
                color: transparent;
                border-color: transparent;
                box-shadow: none;
            `;
        },
        tick: (t, u) => {
            // Fade children
            Array.from(node.children).forEach(child => {
                if (child !== checkmark) {
                    child.style.opacity = Math.max(0, 1 - (u * 4)); // fade out fast
                }
            });
            // Fade in checkmark
            if (u > 0.2 && u < 0.9) {
                checkmark.style.opacity = 1;
            } else {
                checkmark.style.opacity = 0;
            }
        }
    };
}

export function glitchOut(node, { duration = 600 }) {
    return {
        duration,
        css: (t, u) => {
            // t goes from 1 to 0
            if (u < 0.1) return `transform: translate(2px, 0); opacity: 0.9; filter: hue-rotate(90deg);`;
            if (u < 0.2) return `transform: translate(-3px, 2px); opacity: 0.8; filter: hue-rotate(-90deg);`;
            if (u < 0.3) return `transform: translate(4px, -2px) scaleY(0.9); opacity: 0.9; filter: contrast(2);`;
            if (u < 0.4) return `transform: translate(-2px, 0) skewX(10deg); opacity: 0.6; filter: invert(0.2);`;
            if (u < 0.5) return `transform: translate(5px, 2px) skewX(-10deg); opacity: 0.8;`;
            if (u < 0.7) return `transform: scaleY(0.1); opacity: 0.5; background: red;`;
            return `transform: scale(0); opacity: 0;`;
        }
    };
}
