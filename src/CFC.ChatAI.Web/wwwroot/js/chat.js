

window.cfcChat = {
    async selectImages(input, remaining, maxFileBytes, availableBytes) {
        const files = Array.from(input.files || []);
        input.value = "";
        const images = [];
        let error = null;
        for (const file of files) {
            if (!file.size || file.size > maxFileBytes) {
                error = "Choose nonempty images up to 5 MiB each.";
                continue;
            }
            if (images.length >= remaining || file.size > availableBytes) {
                error = "Preview limit reached. Remove a selection or refresh to clear this tab’s previews.";
                continue;
            }
            try {
                const header = new Uint8Array(await file.slice(0, 12).arrayBuffer());
                const chars = String.fromCharCode(...header);
                const valid = (file.type === "image/png" && chars.startsWith("\x89PNG\r\n\x1a\n")) ||
                    (file.type === "image/jpeg" && header[0] === 255 && header[1] === 216 && header[2] === 255) ||
                    (file.type === "image/gif" && /^(GIF87a|GIF89a)/.test(chars)) ||
                    (file.type === "image/webp" && chars.startsWith("RIFF") && chars.slice(8) === "WEBP");
                if (!valid) {
                    error = "Choose PNG, JPEG, GIF or WebP images.";
                    continue;
                }
                images.push({ url: URL.createObjectURL(file), name: file.name, size: file.size });
                availableBytes -= file.size;
            } catch {
                error = "The browser could not read one of the selected images.";
            }
        }
        return { images, error };
    },

    releaseImages(urls) {
        for (const url of urls) URL.revokeObjectURL(url);
    },

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

