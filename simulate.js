import fs from 'fs';
import readline from 'readline';

const API_URL = 'http://localhost:5294/api/debug/inject-log-line';
const DELAY_MS = 50; // Задержка между строками для имитации реального времени

async function simulate(logFilePath) {
    if (!fs.existsSync(logFilePath)) {
        console.error(`Файл не найден: ${logFilePath}`);
        process.exit(1);
    }

    console.log(`🚀 Запуск симуляции логов из: ${logFilePath}`);
    console.log(`Отправка на ${API_URL} с задержкой ${DELAY_MS}мс...`);

    const fileStream = fs.createReadStream(logFilePath);
    const rl = readline.createInterface({
        input: fileStream,
        crlfDelay: Infinity
    });

    let lineCount = 0;

    for await (const line of rl) {
        if (line.trim().length === 0) continue;

        try {
            const response = await fetch(API_URL, {
                method: 'POST',
                headers: {
                    'Content-Type': 'text/plain'
                },
                body: line
            });

            if (!response.ok) {
                console.error(`Ошибка API: ${response.status} ${response.statusText}`);
            }
        } catch (err) {
            console.error(`Ошибка соединения с API: ${err.message}`);
            console.log("Убедитесь, что бэкенд (CitizenRoutePlanner.Api) запущен на http://localhost:5294");
            process.exit(1);
        }

        lineCount++;
        if (lineCount % 50 === 0) {
            console.log(`Отправлено ${lineCount} строк...`);
        }

        // Ждем перед отправкой следующей строки
        await new Promise(resolve => setTimeout(resolve, DELAY_MS));
    }

    console.log(`✅ Симуляция завершена. Всего отправлено строк: ${lineCount}`);
}

const args = process.argv.slice(2);
const logFile = args[0] || 'logs/GameSimulate.log';
simulate(logFile);
