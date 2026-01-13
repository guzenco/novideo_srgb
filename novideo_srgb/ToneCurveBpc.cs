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
                var compensation = _curve_black - _trc_black;
                value = (value - compensation) / (_threshold - compensation) * _threshold;
            }
            return (value - _trc_black) / (1.0 - _trc_black);
        }

        public double SampleInverseAt(double x)
        {
            if (x <= _trc_black) return 0;
            if (x < _threshold)
            {
                var compensation = _curve_black - _trc_black;
                x = (x + compensation) / (_threshold + compensation) * _threshold;
            }
            x = (x + _trc_black) / (1.0 + _trc_black);
            return curve.SampleInverseAt(x);
        }
    }
}