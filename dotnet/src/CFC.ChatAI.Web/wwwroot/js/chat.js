

window.cfcChat = {
    // Automatically scroll to the latest message
    scrollToBottom: function (element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },

    // Jump to a specific timestamp in a video
    seekVideo: function (video, seconds) {
        if (!video || !Number.isFinite(seconds)) return;

        const seek = () => {
            video.currentTime = Math.max(0, seconds);
            video.play().catch(() => {});
        };

        if (video.readyState >= 1) {
            seek();
        } else {
            video.addEventListener("loadedmetadata", seek, {
                once: true
            });
        }
    }
};

