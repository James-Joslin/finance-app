import { Component } from 'react';
import { AlertTriangle, RotateCcw } from 'lucide-react';
import { Link, useLocation } from 'react-router-dom';

class Boundary extends Component {
    state = { error: null };

    static getDerivedStateFromError(error) {
        return { error };
    }

    componentDidCatch(error, info) {
        console.error('Finova route render failed', error, info);
    }

    render() {
        if (!this.state.error) return this.props.children;
        return (
            <div className="page-state error-state" role="alert">
                <AlertTriangle />
                <h2>This page hit an unexpected problem</h2>
                <p>
                    Your data is safe. You can retry the page or continue back
                    to Overview without refreshing Finova.
                </p>
                <div className="modal-actions">
                    <Link className="button secondary" to="/">
                        Back to Overview
                    </Link>
                    <button
                        className="button"
                        type="button"
                        onClick={() => this.setState({ error: null })}
                    >
                        <RotateCcw /> Retry page
                    </button>
                </div>
            </div>
        );
    }
}

export default function RouteErrorBoundary({ children }) {
    const location = useLocation();
    return <Boundary key={location.pathname}>{children}</Boundary>;
}
