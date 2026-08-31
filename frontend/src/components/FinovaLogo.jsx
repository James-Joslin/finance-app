import { staticAssetUrl } from '../lib/staticAssets';

export default function FinovaLogo({ compact = false }) {
    return (
        <div className="finova-logo" aria-label="Finova">
            <span className="finova-logo-mark" aria-hidden="true">
                <img
                    className="finova-logo-plant"
                    src={staticAssetUrl('micro_elements/micro_plant.png')}
                    alt=""
                />
            </span>
            {!compact && <span className="finova-logo-word">Finova</span>}
        </div>
    );
}
