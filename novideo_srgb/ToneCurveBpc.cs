namespace novideo_srgb
{
    public class ToneCurveBpc : ToneCurve
    {
        private ToneCurve curve;

        private double _threshold;
        private double _curve_black;
        private double _trc_black;

        public ToneCurveBpc(ToneCurve curve, double trc_black, double threshold)
        {
            this.curve = curve;
            _curve_black = curve.SampleAt(0);
            _trc_black = trc_black;
            _threshold = threshold;
        }

        public double SampleAt(double x)
        {
            var value = curve.SampleAt(x);
            if(value < _threshold)
            {
                value = (value - _curve_black) / (_threshold - _curve_black) * (_threshold - _trc_black) + _trc_black;
            }
            return (value - _trc_black) / (1.0 - _trc_black);
        }

        public double SampleInverseAt(double x)
        {
            x = x / (1.0 + _trc_black) + _trc_black;
            if (x < _threshold)
            {
                x = (x - _trc_black) / (_threshold - _trc_black) * (_threshold - _curve_black) + _curve_black;
            }
            return curve.SampleInverseAt(x);
        }
    }
}