public class Biquad {
    public double a0, a1, a2, a3, a4;
    public double x1, x2, y1, y2;

    public const int LPF = 0;  // Low pass filter
    public const int HPF = 1;  // High pass filter
    public const int BPF = 2;  // Band pass filter
    public const int NOTCH = 3; // Notch Filter
    public const int PEQ = 4;   // Peaking band EQ filter
    public const int LSH = 5;   // Low shelf filter
    public const int HSH = 6;   // High shelf filter

    private const double M_LN2 = 0.69314718055994530942;
    private const double M_PI = 3.14159265358979323846;

    public double Process(double sample) {
        double result = a0 * sample + a1 * x1 + a2 * x2 - a3 * y1 - a4 * y2;

        // Shift x1 to x2, sample to x1
        x2 = x1;
        x1 = sample;

        // Shift y1 to y2, result to y1
        y2 = y1;
        y1 = result;

        return result;
    }

    public void Init(int type, double dbGain, double freq, double srate, double bandwidth) {
        double A = Math.Pow(10, dbGain / 40);
        double omega = 2 * M_PI * freq / srate;
        double sn = Math.Sin(omega);
        double cs = Math.Cos(omega);
        double alpha = sn * Math.Sinh(M_LN2 / 2 * bandwidth * omega / sn);
        double beta = Math.Sqrt(A + A);

        double a0_coeff, a1_coeff, a2_coeff, b0, b1, b2;

        switch (type) {
            case LPF:
                b0 = (1 - cs) / 2;
                b1 = 1 - cs;
                b2 = (1 - cs) / 2;
                a0_coeff = 1 + alpha;
                a1_coeff = -2 * cs;
                a2_coeff = 1 - alpha;
                break;
            case HPF:
                b0 = (1 + cs) / 2;
                b1 = -(1 + cs);
                b2 = (1 + cs) / 2;
                a0_coeff = 1 + alpha;
                a1_coeff = -2 * cs;
                a2_coeff = 1 - alpha;
                break;
            case BPF:
                b0 = alpha;
                b1 = 0;
                b2 = -alpha;
                a0_coeff = 1 + alpha;
                a1_coeff = -2 * cs;
                a2_coeff = 1 - alpha;
                break;
            case NOTCH:
                b0 = 1;
                b1 = -2 * cs;
                b2 = 1;
                a0_coeff = 1 + alpha;
                a1_coeff = -2 * cs;
                a2_coeff = 1 - alpha;
                break;
            case PEQ:
                b0 = 1 + (alpha * A);
                b1 = -2 * cs;
                b2 = 1 - (alpha * A);
                a0_coeff = 1 + (alpha / A);
                a1_coeff = -2 * cs;
                a2_coeff = 1 - (alpha / A);
                break;
            case LSH:
                b0 = A * ((A + 1) - (A - 1) * cs + beta * sn);
                b1 = 2 * A * ((A - 1) - (A + 1) * cs);
                b2 = A * ((A + 1) - (A - 1) * cs - beta * sn);
                a0_coeff = (A + 1) + (A - 1) * cs + beta * sn;
                a1_coeff = -2 * ((A - 1) + (A + 1) * cs);
                a2_coeff = (A + 1) + (A - 1) * cs - beta * sn;
                break;
            case HSH:
                b0 = A * ((A + 1) + (A - 1) * cs + beta * sn);
                b1 = -2 * A * ((A - 1) + (A + 1) * cs);
                b2 = A * ((A + 1) + (A - 1) * cs - beta * sn);
                a0_coeff = (A + 1) - (A - 1) * cs + beta * sn;
                a1_coeff = 2 * ((A - 1) - (A + 1) * cs);
                a2_coeff = (A + 1) - (A - 1) * cs - beta * sn;
                break;
            default:
                return;
        }

        // Precompute the coefficients
        a0 = b0 / a0_coeff;
        a1 = b1 / a0_coeff;
        a2 = b2 / a0_coeff;
        a3 = a1_coeff / a0_coeff;
        a4 = a2_coeff / a0_coeff;

        // Zero initial samples
        x1 = x2 = 0;
        y1 = y2 = 0;
    }
}

