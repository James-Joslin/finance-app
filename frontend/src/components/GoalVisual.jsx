import {
    Armchair, Baby, Bike, BriefcaseBusiness, CarFront, Gift, GraduationCap,
    HandCoins, HeartHandshake, HeartPulse, House, Landmark, Laptop, PackageOpen,
    Paintbrush, Palette, Palmtree, PawPrint, Plane, ReceiptText, ShieldCheck,
    Target, Ticket, Umbrella,
} from 'lucide-react';

const definitions = [
    ['general_target', 'General goal', 'General', ['target', 'saving'], Target],
    ['emergency_fund', 'Emergency fund', 'Safety', ['emergency', 'buffer'], ShieldCheck],
    ['rainy_day', 'Rainy day', 'Safety', ['rain', 'unexpected'], Umbrella],
    ['house_deposit', 'House deposit', 'Home', ['home', 'mortgage'], House],
    ['renovation', 'Renovation', 'Home', ['paint', 'improvement'], Paintbrush],
    ['moving', 'Moving home', 'Home', ['move', 'boxes'], PackageOpen],
    ['furniture', 'Furniture', 'Home', ['chair', 'interior'], Armchair],
    ['car', 'Car', 'Transport', ['vehicle', 'drive'], CarFront],
    ['bicycle', 'Bicycle', 'Transport', ['bike', 'cycle'], Bike],
    ['travel', 'Travel', 'Experiences', ['flight', 'trip'], Plane],
    ['holiday', 'Holiday', 'Experiences', ['beach', 'vacation'], Palmtree],
    ['wedding', 'Wedding', 'Life', ['marriage', 'celebration'], HeartHandshake],
    ['baby_family', 'Baby & family', 'Life', ['child', 'family'], Baby],
    ['education', 'Education', 'Future', ['study', 'university'], GraduationCap],
    ['health', 'Health', 'Wellbeing', ['medical', 'care'], HeartPulse],
    ['pet', 'Pet', 'Life', ['animal', 'dog', 'cat'], PawPrint],
    ['technology', 'Technology', 'Things', ['laptop', 'computer'], Laptop],
    ['hobby', 'Hobby', 'Experiences', ['creative', 'craft'], Palette],
    ['event', 'Event', 'Experiences', ['ticket', 'concert'], Ticket],
    ['gift', 'Gift', 'Life', ['present', 'birthday'], Gift],
    ['business', 'Business', 'Future', ['company', 'startup'], BriefcaseBusiness],
    ['debt_payoff', 'Debt payoff', 'Finance', ['loan', 'debt'], HandCoins],
    ['tax', 'Tax', 'Finance', ['receipt', 'hmrc'], ReceiptText],
    ['retirement', 'Retirement', 'Future', ['pension', 'future'], Landmark],
];

export const goalVisuals = definitions.map(([key, title, category, searchTerms, Icon]) => ({
    key, title, category, searchTerms, Icon,
}));

const colors = {
    blue: ['#168bff', '#77c6ff'],
    cyan: ['#19b6c9', '#55e0d2'],
    mint: ['#20b98d', '#7be1ba'],
    violet: ['#7867eb', '#b8a9ff'],
    coral: ['#ec735d', '#ffb29e'],
    amber: ['#d8942e', '#ffd184'],
    rose: ['#d95b8f', '#ffaed0'],
    slate: ['#5d7896', '#abc0d5'],
};

export function GoalVisual({ iconKey = 'general_target', colorKey = 'blue', imageUrl, size = 'card', label }) {
    const definition = goalVisuals.find((item) => item.key === iconKey) || goalVisuals[0];
    const Icon = definition.Icon;
    const palette = colors[colorKey] || colors.blue;
    if (imageUrl) {
        return <img className={'goal-visual goal-visual-' + size + ' goal-image'} src={imageUrl} alt={label || definition.title} />;
    }
    return (
        <div
            className={'goal-visual goal-visual-' + size}
            style={{ '--goal-primary': palette[0], '--goal-secondary': palette[1] }}
            role="img"
            aria-label={label || definition.title}
        >
            <svg className="goal-vector-art" viewBox="0 0 400 120" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
                <path d="M-18 104 C58 49 121 132 205 77 C278 29 331 111 418 54 V138 H-18Z" fill="var(--goal-secondary)" opacity=".14" />
                <path d="M-18 100 C58 45 121 128 205 73 C278 25 331 107 418 50" fill="none" stroke="var(--goal-primary)" strokeWidth="2" strokeOpacity=".42" vectorEffect="non-scaling-stroke" />
                <circle cx="334" cy="25" r="24" fill="var(--goal-secondary)" opacity=".24" />
            </svg>
            <span className="goal-icon-orbit"><Icon strokeWidth={1.8} /></span>
        </div>
    );
}

export function GoalIconPicker({ value, onChange }) {
    return (
        <div className="goal-icon-picker" role="radiogroup" aria-label="Goal icon">
            {goalVisuals.map((item) => (
                <button
                    type="button"
                    role="radio"
                    aria-checked={value === item.key}
                    aria-label={item.title}
                    title={item.title}
                    className={value === item.key ? 'selected' : ''}
                    key={item.key}
                    onClick={() => onChange(item.key)}
                >
                    <item.Icon />
                </button>
            ))}
        </div>
    );
}

export const goalColors = Object.keys(colors);
