window.cookieStorage = {
    getItem(key) {
        const cookies = document.cookie.split(";");
        for (let i = 0; i < cookies.length; i++) {
            const cookie = cookies[i].split("=");
            if (key === cookie[0].trim()) {
                return decodeURIComponent(cookie[1]);
            }
        }
        return null;
    },
    setItem(key, value) {
        // biome-ignore lint/suspicious/noDocumentCookie: I do not care
        document.cookie = `${key} = ${encodeURIComponent(value)}`;
    },
};
