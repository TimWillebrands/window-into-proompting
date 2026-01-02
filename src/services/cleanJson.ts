export function cleanJsonString(jsonString: string): string {
    const json = jsonString.trim();

    if (json.startsWith("```")) {
        const lines = json.split("\n");
        const end = lines.findLastIndex((line) => line.startsWith("```"));
        const cleanedJson = lines.slice(1, end).join("\n");
        console.log("Cleaned JSON:", cleanedJson);
        return cleanedJson;
    }

    if (json.startsWith("{") && !json.endsWith("}")) {
        let cleanedJson = json;
        if (json.endsWith(",")) {
            cleanedJson = cleanedJson.slice(0, -1);
        }
        cleanedJson += "}";
        console.log("Cleaned JSON:", cleanedJson);
        return cleanedJson;
    }

    return json;
}
