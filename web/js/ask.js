// Ask/Chat feature
import { appendMsg, appendVideoCard } from './helpers.js';

export function initAsk({ askFormId, askQuestionId, askSendId, askThreadId }) {
  const askForm = document.getElementById(askFormId);
  const askQuestion = document.getElementById(askQuestionId);
  const askSend = document.getElementById(askSendId);
  const askThread = document.getElementById(askThreadId);

  if (!askForm || !askQuestion || !askSend || !askThread) return;

  askForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const q = askQuestion.value.trim();
    if (!q) return;

    askQuestion.value = '';
    appendMsg(askThread, q, 'user');
    const typing = appendMsg(askThread, 'Assistant is typing', 'bot', 'typing dots');
    askSend.disabled = true;

    try {
      const res = await fetch('/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: q, top_k: 4 }),
      });
      const data = await res.json();
      typing.remove();

      if (data.success) {
        appendMsg(askThread, data.answer || 'No answer available', 'bot');
        if (Array.isArray(data.video_context)) {
          data.video_context.forEach((clip) => appendVideoCard(askThread, clip));
        }
      } else {
        appendMsg(askThread, data.detail || 'Error', 'bot');
      }
    } catch (err) {
      typing.remove();
      appendMsg(askThread, String(err), 'bot');
    } finally {
      askSend.disabled = false;
    }
  });
}
