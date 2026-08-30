const STATIC_ASSET_ROOT = `${import.meta.env.BASE_URL}static/`;

export function staticAssetUrl(path) {
    return `${STATIC_ASSET_ROOT}${path.replace(/^\/+/, '')}`;
}
